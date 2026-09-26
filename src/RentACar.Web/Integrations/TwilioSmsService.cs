using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RentACar.Application.Integrations;

namespace RentACar.Web.Integrations;

/// <summary>
/// Gerçek Twilio SMS göndericisi (Messages API). Config-gated: <c>Twilio:AccountSid</c> VE bir gönderen
/// kaynağı (<c>Twilio:SmsFrom</c> ya da <c>Twilio:MessagingServiceSid</c>) varsa Program.cs DI'da
/// <c>StubSmsService</c>'i override eder.
///
/// <para><b>WhatsApp göndericisiyle aynı hesabı kullanır</b> — ayrı bir SMS sağlayıcısı sözleşmesi
/// gerekmez. Fark uçta değil gönderen alanında: WhatsApp <c>whatsapp:</c> önekli, SMS çıplak numara
/// ya da alfanümerik başlıktır.</para>
///
/// <para><b>Gönderen önceliği:</b> (1) çağıranın verdiği <c>gonderen</c> — tenant'ın kendi SMS başlığı,
/// (2) <c>Twilio:SmsFrom</c> numarası, (3) <c>Twilio:MessagingServiceSid</c>. Messaging Service kullanılıyorsa
/// <c>From</c> yerine <c>MessagingServiceSid</c> gönderilir (Twilio ikisini birlikte kabul etmez).</para>
///
/// <para><b>Türkiye uyarısı:</b> alfanümerik başlık (ör. "RENTPRO") operatör kaydı ister; kayıtsız
/// başlıkla gönderilen mesaj Twilio'ya kabul edilse de teslim edilmez. Kayıt yoksa numara ile gönderin.</para>
///
/// <para>Hata/timeout(10sn) → <c>false</c> (çağıran job ÇÖKMESİN). 201 Created teslim demek DEĞİLDİR —
/// <see cref="TwilioWhatsAppService.LastStatusAsync"/> ile aynı gerekçe; ilk durum loglanır, kesin
/// doğrulama <see cref="LastStatusAsync"/> ile yapılır.</para>
/// </summary>
public sealed class TwilioSmsService(
    IHttpClientFactory httpFactory, IConfiguration config, ILogger<TwilioSmsService> log) : ISmsService
{
    public async Task<bool> SendAsync(
        string phone, string message, string? sender = null, CancellationToken ct = default)
        => (await GonderAsync(phone, message, sender, ct)).Ok;

    /// <summary>Gönderim + oluşan mesajın SID'i (teşhis yolu için — bkz. WhatsApp göndericisindeki gerekçe).</summary>
    public async Task<(bool Ok, string? Sid)> GonderAsync(
        string phone, string message, string? sender = null, CancellationToken ct = default)
    {
        var sid = config["Twilio:AccountSid"];
        var token = config["Twilio:AuthToken"];
        var smsFrom = config["Twilio:SmsFrom"];
        var msgServiceSid = config["Twilio:MessagingServiceSid"];

        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token))
        {
            log.LogWarning("Twilio kimliği eksik → SMS gönderilmedi.");
            return (false, null);
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            log.LogWarning("Boş SMS gövdesi → gönderilmedi.");
            return (false, null);
        }

        var from = !string.IsNullOrWhiteSpace(sender) ? sender.Trim() : smsFrom;
        if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(msgServiceSid))
        {
            log.LogWarning("Twilio SMS gönderen kaynağı yok (Twilio:SmsFrom / MessagingServiceSid / tenant başlığı) → gönderilmedi.");
            return (false, null);
        }

        var body = new Dictionary<string, string> { ["To"] = phone, ["Body"] = message };
        // From ve MessagingServiceSid birlikte gönderilemez — açık gönderen varsa o kazanır.
        if (!string.IsNullOrWhiteSpace(from)) body["From"] = from;
        else body["MessagingServiceSid"] = msgServiceSid!;

        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json")
            {
                Content = new FormUrlEncodedContent(body),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}")));

            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                log.LogWarning("Twilio SMS başarısız {Status}: {Err}", (int)resp.StatusCode, err);
                return (false, null);
            }

            string? createdSid = null;
            try
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;
                createdSid = root.TryGetProperty("sid", out var s2) ? s2.GetString() : null;
                var status = root.TryGetProperty("status", out var d2) ? d2.GetString() : null;
                var error = root.TryGetProperty("error_code", out var e2) && e2.ValueKind != JsonValueKind.Null
                    ? e2.ToString() : null;
                if (status is "failed" or "undelivered" || error is not null)
                {
                    log.LogWarning("Twilio SMS oluşturuldu ama BAŞARISIZ (sid={Sid} durum={Durum} hata={Hata}).",
                        createdSid, status, error);
                    return (false, createdSid);
                }
                log.LogInformation("Twilio SMS kuyruğa alındı (sid={Sid} durum={Durum}).", createdSid, status);
            }
            catch (JsonException) { /* gövde okunamadı — kabul yanıtını geçerli say */ }
            return (true, createdSid);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Twilio SMS gönderim hatası.");
            return (false, null);
        }
    }

    /// <summary>Mesajın gerçek teslim durumunu SID ile okur (teşhis — Ayarlar test butonu).</summary>
    public async Task<(string? Durum, string? HataKodu)> LastStatusAsync(string messageSid, CancellationToken ct = default)
    {
        var sid = config["Twilio:AccountSid"];
        var token = config["Twilio:AuthToken"];
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(messageSid)) return (null, null);
        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages/{messageSid}.json");
            req.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}")));
            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Twilio SMS durum sorgusu HTTP {Kod} döndü.", (int)resp.StatusCode);
                return (null, null);
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var m = doc.RootElement;
            var status = m.TryGetProperty("status", out var d) ? d.GetString() : null;
            var error = m.TryGetProperty("error_code", out var e) && e.ValueKind != JsonValueKind.Null
                ? e.ToString() : null;
            return (status, error);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Twilio SMS durum sorgusu başarısız.");
            return (null, null);
        }
    }

    /// <summary>Twilio SMS hata kodunu operatörün anlayacağı cümleye çevirir.</summary>
    /// <remarks>Saf ve public — doğrudan test edilebilsin diye (repo deseni).</remarks>
    public static string ErrorDescription(string? code) => code switch
    {
        "21211" or "21212" => "Numara biçimi geçersiz (E.164 olmalı, ör. +905321112233).",
        "21408" => "Bu ülkeye SMS gönderim izni kapalı. Twilio konsolunda Geographic Permissions "
                 + "bölümünden Türkiye'yi açın.",
        "21610" => "Alıcı bu gönderene mesaj gelmesini durdurmuş (STOP).",
        "21612" => "Seçilen gönderen numarasından bu alıcıya SMS iletilemiyor.",
        "21659" => "Gönderen numara bu Twilio hesabına ait değil ya da SMS için uygun değil.",
        "30007" => "Operatör mesajı spam filtresine takıldı. Alfanümerik başlık kullanıyorsanız "
                 + "başlığın Türkiye'de kayıtlı olması gerekir.",
        "30008" => "Teslim edilemedi (operatör sebebi bildirmedi).",
        "20003" => "Twilio kimliği geçersiz (Account SID / Auth Token).",
        null => "",
        _ => $"Twilio hata kodu {code}.",
    };
}
