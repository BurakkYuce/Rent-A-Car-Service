using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using MailKit.Security;
using MimeKit;
using RentACar.Application.Integrations;

namespace RentACar.Infrastructure.Integrations;

/// <summary>
/// Gerçek SMTP göndericisi (MailKit). <see cref="NoopEmailSender"/>'ı Program.cs DI'da KOŞULSUZ
/// override eder — çünkü yapılandırma global config'te değil TENANT BAŞINA veritabanındadır
/// (<c>TenantSettings.Smtp*</c>); "yapılandırılmış mı" sorusunu gönderim anında ayarın kendisi
/// cevaplar, DI kaydı değil.
///
/// <para><b>Şifreleme seçimi</b> porta bilinçli olarak bağlıdır, tek bir <c>Ssl</c> bayrağına değil:
/// 465 = örtük TLS (SslOnConnect), 587/25 = STARTTLS. Tek bayrakla 587'ye SslOnConnect denemek
/// yaygın ve teşhisi zor bir "bağlantı asılı kalıyor" hatasıdır. Bayrak yalnız STARTTLS'in
/// ZORUNLU mu yoksa opsiyonel mi olduğunu belirler.</para>
///
/// <para>Zaman aşımı 20 sn: SMTP el sıkışması HTTP'den yavaştır, ama bir arka plan işi burada
/// süresiz asılı kalmamalıdır. Hata → <c>Ok=false</c> + operatörün okuyabileceği Türkçe cümle;
/// istisna YUKARI SIZMAZ (çağıran job çökmesin).</para>
/// </summary>
public sealed class MailKitEmailSender(
    ILogger<MailKitEmailSender> log,
    Func<string, CancellationToken, Task<System.Net.IPAddress?>>? endpointResolver = null) : IEmailSender
{
    /// <summary>Hedef çözücü: üretimde <see cref="SmtpEndpointGuard.ResolveAllowedAsync"/> (iç ağ reddi). Test yerel sahte
    /// sunucuya bağlanmak için kendi çözücüsünü verir (DI bu parametreyi vermez → varsayılan).</summary>
    private readonly Func<string, CancellationToken, Task<System.Net.IPAddress?>> _resolve =
        endpointResolver ?? SmtpEndpointGuard.ResolveAllowedAsync;

    public async Task<EpostaSonuc> SendAsync(SmtpAyar setting, EpostaMesaj message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(setting.Host))
            return new EpostaSonuc(false, "SMTP sunucusu tanımlı değil (Ayarlar → E-posta).");
        if (setting.Port is <= 0 or > 65535)
            return new EpostaSonuc(false, $"SMTP portu geçersiz: {setting.Port}.");
        // F11.1b güvenlik M4: yalnız SMTP portları (kiracı ayarı sunucumuzu port tarayıcısına çeviremez).
        if (!SmtpEndpointGuard.AllowedPorts.Contains(setting.Port))
            return new EpostaSonuc(false, "SMTP portu yalnız 25, 465, 587 ya da 2525 olabilir.");
        if (string.IsNullOrWhiteSpace(setting.GonderenAdres))
            return new EpostaSonuc(false, "Gönderen e-posta adresi tanımlı değil (Ayarlar → E-posta).");
        if (string.IsNullOrWhiteSpace(message.Alici))
            return new EpostaSonuc(false, "Alıcı e-posta adresi boş.");

        MimeMessage mime;
        try
        {
            mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(setting.GonderenAd ?? setting.GonderenAdres, setting.GonderenAdres));
            mime.To.Add(MailboxAddress.Parse(message.Alici));
            mime.Subject = message.Konu;
            mime.Body = new BodyBuilder
            {
                HtmlBody = message.GovdeHtml,
                // Düz metin gövdesi verilmezse HTML'i olduğu gibi koymayız (etiketler görünür);
                // düz-metin alternatifini çağıran üretir, yoksa yalnız HTML gider.
                TextBody = message.GovdeDuz,
            }.ToMessageBody();
        }
        catch (ParseException)
        {
            return new EpostaSonuc(false, $"E-posta adresi geçersiz: {message.Alici}");
        }

        try
        {
            // F11.1b güvenlik M4: DNS çözümünden SONRA iç ağ/loopback/link-local reddi; bağlantı denetlenen IP'ye açılır.
            var address = await _resolve(setting.Host, ct);
            if (address is null)
            {
                log.LogWarning("SMTP hedefi reddedildi ya da çözülemedi ({Host}:{Port}).", setting.Host, setting.Port);
                return new EpostaSonuc(false, GenericFailure);
            }

            using var client = new SmtpClient { Timeout = 20_000 };
            var secure = setting.Port == 465
                ? SecureSocketOptions.SslOnConnect
                : setting.Ssl ? SecureSocketOptions.StartTls : SecureSocketOptions.StartTlsWhenAvailable;

            var socket = new System.Net.Sockets.Socket(address.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectTimeout.CancelAfter(20_000);
                try
                {
                    await socket.ConnectAsync(new System.Net.IPEndPoint(address, setting.Port), connectTimeout.Token);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
            await client.ConnectAsync(socket, setting.Host, setting.Port, secure, ct);
            if (!string.IsNullOrWhiteSpace(setting.Kullanici))
            {
                // F11.1b güvenlik M3: şifreli olmayan bağlantıda AUTH YOK — parola düz metin gitmez.
                if (!client.IsSecure)
                {
                    await client.DisconnectAsync(true, ct);
                    return new EpostaSonuc(false,
                        "SMTP sunucusu şifreli bağlantı (TLS) sunmuyor; parola şifresiz gönderilmez. 465 (SSL) ya da 587 (STARTTLS) kullanın.");
                }
                await client.AuthenticateAsync(setting.Kullanici, setting.Sifre ?? string.Empty, ct);
            }
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);

            log.LogInformation("E-posta gönderildi (alici={Alici} konu={Konu}).", message.Alici, message.Konu);
            return new EpostaSonuc(true, null);
        }
        catch (AuthenticationException ex)
        {
            log.LogWarning(ex, "SMTP kimlik doğrulaması başarısız.");
            return new EpostaSonuc(false, "SMTP kullanıcı adı veya şifresi kabul edilmedi.");
        }
        catch (SslHandshakeException ex)
        {
            log.LogWarning(ex, "SMTP TLS el sıkışması başarısız.");
            return new EpostaSonuc(false,
                $"TLS el sıkışması başarısız. {setting.Host}:{setting.Port} için şifreleme ayarını kontrol edin "
                + "(465 → SSL, 587 → STARTTLS).");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("SMTP zaman aşımı ({Host}:{Port}).", setting.Host, setting.Port);
            return new EpostaSonuc(false, GenericFailure);
        }
        catch (Exception ex)
        {
            // F11.1b güvenlik M4: ham istisna metni (bağlantı reddedildi / ulaşılamadı / sunucu banner'ı) kullanıcıya
            // DÖNMEZ — port tarama sinyali olurdu; ayrıntı log'da.
            log.LogWarning(ex, "SMTP gönderim hatası ({Host}:{Port}).", setting.Host, setting.Port);
            return new EpostaSonuc(false, GenericFailure);
        }
    }

    /// <summary>Bağlantı/çözüm/zaman aşımı hatalarının TEK kullanıcı mesajı (ayrım yok → ağ keşfi sinyali yok).</summary>
    public const string GenericFailure =
        "E-posta gönderilemedi. SMTP sunucu adı, port ve şifreleme ayarlarını kontrol edin; ayrıntı sunucu kayıtlarında.";
}
