using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Web.Integrations;

namespace RentACar.Web.Api.Sistem;

public static partial class SystemAdminApi
{
    private static readonly Regex E164 = new(@"^\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant);

    /// <summary>Teslim yoklaması: 9 × 1 sn (Blazor ucuyla aynı; canlıda teslim hatası ~5-8 sn içinde düştü).</summary>
    private const int DeliveryPollCount = 9;

    /// <summary>
    /// Test gönderimleri (Blazor <c>/ayarlar/{smtp,sms,whatsapp}-test</c> paritesi) — ÜRETİMDEKİ kod yolu: e-posta
    /// <see cref="BildirimKanaliService.EpostaGonderAsync"/>, SMS başlığı <see cref="BildirimKanaliService.SmsBaslikAsync"/>
    /// + <see cref="TwilioSmsService"/>, WhatsApp <c>operasyon_ozet</c> şablonuyla <see cref="TwilioWhatsAppService"/>.
    /// Dürüst stub kuralı: Twilio yapılandırması yoksa hiçbir şey gönderilmez ve <c>yapilandirma_yok</c> döner;
    /// "kabul edildi" teslim demek değildir → durum yoklanır, belli değilse <c>belirsiz</c> (başarı DEĞİL).
    /// </summary>
    private static void MapSendTests(RouteGroupBuilder g)
    {
        g.MapPost("/test/eposta", async Task<Ok<SendTestResult>> (EmailTestRequest i, BildirimKanaliService channel, CancellationToken ct) =>
        {
            var to = RequireEmail(i.Alici);
            var time = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var r = await channel.EpostaGonderAsync(to, "RentPro e-posta testi",
                $"<p>RentPro test mesajı — {time} UTC.</p><p>Bu mesajı aldıysanız e-posta yapılandırmanız çalışıyor.</p>",
                $"RentPro test mesajı — {time} UTC. Bu mesajı aldıysanız e-posta yapılandırmanız çalışıyor.", ct);
            // SMTP gönderimi senkron: sunucu kabul ettiyse teslim sorumluluğu ona geçmiştir.
            return TypedResults.Ok(r.Ok
                ? new SendTestResult(true, "gonderildi", "Test e-postası SMTP sunucusuna teslim edildi.")
                : new SendTestResult(false, "basarisiz", r.Hata ?? "E-posta gönderilemedi."));
        }).AlanlariEsle([("E-posta adresi", "alici")]).RequireRateLimiting(SendTestRatePolicy);

        g.MapPost("/test/sms", async Task<Ok<SendTestResult>> (PhoneTestRequest i, BildirimKanaliService channel,
            ISmsService sms, IConfiguration cfg, CancellationToken ct) =>
        {
            var to = RequirePhone(i.Telefon);
            if (string.IsNullOrWhiteSpace(cfg["Twilio:AccountSid"]))
                return TypedResults.Ok(new SendTestResult(false, "yapilandirma_yok",
                    "Twilio yapılandırılmamış — hiçbir SMS gönderilmedi. Twilio:AccountSid / AuthToken ve Twilio:SmsFrom (ya da MessagingServiceSid) ayarlarını verin."));
            var message = $"RentPro test mesaji - {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC. Bu mesajı aldıysanız SMS yapılandırmanız çalışıyor.";
            var sender = await channel.SmsBaslikAsync(ct);
            var twilio = sms as TwilioSmsService;
            var (ok, messageSid) = twilio is not null ? await twilio.GonderAsync(to, message, sender, ct) : (await sms.SendAsync(to, message, sender, ct), null);
            if (!ok)
                return TypedResults.Ok(new SendTestResult(false, "basarisiz",
                    "SMS gönderilemedi. Twilio yapılandırmasını, gönderen başlığını ve sunucu loglarını kontrol edin."));
            if (twilio is null || string.IsNullOrEmpty(messageSid))
                return TypedResults.Ok(new SendTestResult(false, "belirsiz", "SMS iletildi, teslim durumu doğrulanamadı."));
            return TypedResults.Ok(await PollAsync(c => twilio.SonDurumAsync(messageSid, c), ["delivered", "sent"],
                TwilioSmsService.HataAciklama, "SMS", ct));
        }).AlanlariEsle([("Telefon", "telefon")]).RequireRateLimiting(SendTestRatePolicy);

        g.MapPost("/test/whatsapp", async Task<Ok<SendTestResult>> (PhoneTestRequest i, IWhatsAppService wa, IConfiguration cfg, CancellationToken ct) =>
        {
            var to = RequirePhone(i.Telefon);
            if (string.IsNullOrWhiteSpace(cfg["Twilio:AccountSid"]))
                return TypedResults.Ok(new SendTestResult(false, "yapilandirma_yok",
                    "Twilio yapılandırılmamış — test modundasınız, hiçbir mesaj gönderilmedi. Twilio:AccountSid / AuthToken / WhatsAppFrom ayarlarını verin."));
            var message = $"RentPro test mesajı — {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC. Bu mesajı aldıysanız WhatsApp yapılandırmanız çalışıyor.";
            var pars = new Dictionary<string, string> { ["1"] = message };
            var twilio = wa as TwilioWhatsAppService;
            var (ok, messageSid) = twilio is not null
                ? await twilio.GonderAsync(to, "operasyon_ozet", pars, ct)
                : (await wa.SendTemplateAsync(to, "operasyon_ozet", pars, ct), null);
            if (!ok)
                return TypedResults.Ok(new SendTestResult(false, "basarisiz",
                    "WhatsApp gönderilemedi. Twilio yapılandırmasını ve sunucu loglarını kontrol edin."));
            if (twilio is null || string.IsNullOrEmpty(messageSid))
                return TypedResults.Ok(new SendTestResult(false, "belirsiz", "WhatsApp mesajı iletildi, teslim durumu doğrulanamadı."));
            return TypedResults.Ok(await PollAsync(c => twilio.SonDurumAsync(messageSid, c), ["delivered", "read"],
                TwilioWhatsAppService.HataAciklama, "Mesaj", ct));
        }).AlanlariEsle([("Telefon", "telefon")]).RequireRateLimiting(SendTestRatePolicy);
    }

    /// <summary>
    /// F11.1b güvenlik M4/M6 — dış çağrı yapan ayar eylemleri (SMTP bağlantısı, ücretli SMS/WhatsApp, alan adı
    /// ekle/doğrula) için AYRI hız sınırı kovası (Program.cs; IP başına dakikada <c>RateLimit:ExternalActionPermit</c>,
    /// varsayılan 20). Giriş kovasından ayrıdır: bu eylemler girişi kilitlemez. Blazor karşılıkları da aynı politikayı taşır.
    /// </summary>
    public const string ExternalActionRatePolicy = "external-actions";

    internal const string SendTestRatePolicy = ExternalActionRatePolicy;

    private static async Task<SendTestResult> PollAsync(
        Func<CancellationToken, Task<(string? Durum, string? HataKodu)>> status, string[] delivered,
        Func<string?, string> explain, string label, CancellationToken ct)
    {
        for (var n = 0; n < DeliveryPollCount; n++)
        {
            await Task.Delay(1000, ct);
            var (state, code) = await status(ct);
            if (state is "failed" or "undelivered")
                return new SendTestResult(false, "teslim_edilemedi",
                    $"{label} Twilio'ya iletildi ama TESLİM EDİLEMEDİ ({state}). {explain(code)}");
            if (state is not null && delivered.Contains(state))
                return new SendTestResult(true, "gonderildi", $"{label} teslim edildi ({state}).");
        }
        return new SendTestResult(false, "belirsiz",
            $"{label} Twilio'ya iletildi, teslim durumu henüz belli değil. Birkaç saniye sonra telefonu ve Twilio konsolunu kontrol edin.");
    }

    private static string RequirePhone(string? phone)
    {
        var p = (phone ?? "").Trim();
        if (!E164.IsMatch(p))
            throw new ValidationException("Telefon numarası E.164 biçiminde olmalıdır (ör. +905321112233).", "telefon");
        return p;
    }

    private static string RequireEmail(string? email)
    {
        var e = (email ?? "").Trim();
        if (e.Length is 0 or > 254 || !System.Net.Mail.MailAddress.TryCreate(e, out var a) || a.Address != e)
            throw new ValidationException("E-posta adresi geçerli değil.", "alici");
        return e;
    }
}
