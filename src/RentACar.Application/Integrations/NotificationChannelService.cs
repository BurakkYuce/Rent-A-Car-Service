using RentACar.Application.Common;
using RentACar.Application.TenantSettings;

namespace RentACar.Application.Integrations;

/// <summary>
/// Tenant'ın bildirim kanallarını (e-posta / SMS) tek yerden kullanılabilir kılar: ayar satırını okur,
/// sırları çözer ve gönderiye çevirir.
///
/// <para><b>Neden <see cref="TenantSettingsService"/> kullanılmıyor:</b> o servis ayarları hassas kabul
/// edip <c>ManageUsers</c> izni arar. Bildirimi tetikleyen akışlar (rezervasyon onayı, vade hatırlatması,
/// arka plan işleri) operatör yetkisiyle ya da hiç kullanıcı bağlamı olmadan çalışır — oradan geçmek
/// gönderimi yetki hatasıyla düşürürdü. Bu servis ayarı DOĞRUDAN repository'den okur; tenant izolasyonu
/// zaten query filter + RLS ile sağlanır ve dışarı hiçbir sır sızdırmaz (yalnız gönderim yapar).</para>
///
/// <para><b>Yapılandırma yoksa sessiz başarı YOKTUR</b> — gönderilemeyen mesaj açık bir hata cümlesiyle
/// döner, çağıran bunu kaydeder/gösterir.</para>
/// </summary>
public sealed class NotificationChannelService(
    ITenantSettingsRepository settings, ISecretProtector secrets, IEmailSender email, ISmsService sms)
{
    /// <summary>Tenant'ın SMTP ayarını çözer. Eksikse (host/gönderen yok) <c>null</c> döner.</summary>
    public async Task<SmtpAyar?> SmtpSettingAsync(CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        if (s is null || string.IsNullOrWhiteSpace(s.SmtpHost)) return null;

        // Gönderen adresi ayrı bir alandır; verilmemişse SMTP kullanıcı adı e-posta biçimindeyse ona
        // düşeriz (yaygın kurulum). İkisi de yoksa gönderim yapılamaz — uydurma adres ÜRETİLMEZ,
        // çünkü geçersiz From alanı sessizce spam'e düşen mesajlar üretir.
        var senderAddress = Empty(s.SmtpGonderenAdres)
            ?? (s.SmtpKullanici?.Contains('@') == true ? s.SmtpKullanici.Trim() : null);
        if (senderAddress is null) return null;

        return new SmtpAyar(
            Host: s.SmtpHost.Trim(),
            Port: s.SmtpPort is > 0 and <= 65535 ? s.SmtpPort.Value : 587,
            Ssl: s.SmtpSsl ?? true,
            Kullanici: Empty(s.SmtpKullanici),
            Sifre: secrets.Unprotect(s.SmtpSifreEnc),
            GonderenAdres: senderAddress,
            GonderenAd: Empty(s.SmtpGonderenAd) ?? Empty(s.FirmaUnvan));
    }

    /// <summary>Tenant ayarıyla e-posta gönderir. Ayar yoksa <c>Ok=false</c> + açıklama döner.</summary>
    public async Task<EpostaSonuc> SendEmailAsync(
        string recipient, string subject, string bodyHtml, string? bodyPlain = null, CancellationToken ct = default)
    {
        var setting = await SmtpSettingAsync(ct);
        if (setting is null)
            return new EpostaSonuc(false,
                "E-posta gönderilemedi: SMTP ayarları eksik (Ayarlar → E-posta: sunucu ve gönderen adresi).");
        return await email.SendAsync(setting, new EpostaMesaj(recipient, subject, bodyHtml, bodyPlain), ct);
    }

    /// <summary>
    /// Tenant başlığıyla SMS gönderir. Başlık (<c>TenantSettings.SmsBaslik</c>) doluysa gönderen olarak
    /// kullanılır; boşsa sağlayıcı varsayılanına düşülür.
    /// </summary>
    public async Task<bool> SmsGonderAsync(string phone, string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(message)) return false;
        return await sms.SendAsync(phone.Trim(), message, await SmsHeaderAsync(ct), ct);
    }

    /// <summary>
    /// Tenant'ın SMS gönderen başlığı (boşsa null → sağlayıcı varsayılanı kullanılır).
    /// Ayrı metot: teşhis ucu gönderimi doğrudan gerçek gönderici üzerinden yapıp mesaj SID'ini
    /// almak zorunda (teslim doğrulaması SID ile sorulur), ama başlığı yine buradan çözmeli —
    /// aksi halde test, üretimden farklı bir gönderenle çalışırdı.
    /// </summary>
    public async Task<string?> SmsHeaderAsync(CancellationToken ct = default)
        => Empty((await settings.GetAsync(ct))?.SmsBaslik);

    private static string? Empty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
