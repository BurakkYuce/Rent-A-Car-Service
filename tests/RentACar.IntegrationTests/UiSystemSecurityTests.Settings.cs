using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiSystemSecurityTests
{
    private const string Settings = V1 + "/ayarlar";

    private static object Smtp(string? version, string host, int port, string? password)
        => new { smtpHost = host, smtpPort = port, smtpKullanici = "mailer", smtpSifre = password, surum = version };

    // M3 — SMTP sunucusu değiştirilip parola boş bırakılarak kayıtlı parola saldırgan sunucuya gönderilemez.
    [Fact]
    public async Task Changing_smtp_target_requires_reentering_the_password()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var version = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();
        var saved = await Json(await Send(admin, HttpMethod.Put, Settings, Smtp(version, "smtp.firma.test", 587, "ilk-" + Random("p"))));
        var cipher = await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => x.SmtpSifreEnc).FirstAsync());
        version = saved.GetProperty("surum").GetString();

        await Problem(await Send(admin, HttpMethod.Put, Settings, Smtp(version, "smtp.saldirgan.test", 587, null)),
            HttpStatusCode.BadRequest, "dogrulama", "smtpSifre");
        await Problem(await Send(admin, HttpMethod.Put, Settings, Smtp(version, "smtp.firma.test", 465, null)),
            HttpStatusCode.BadRequest, "dogrulama", "smtpSifre");
        var after = await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => new { x.SmtpHost, x.SmtpSifreEnc }).FirstAsync());
        Assert.Equal("smtp.firma.test", after.SmtpHost);
        Assert.Equal(cipher, after.SmtpSifreEnc);

        // Aynı hedef + boş parola: korunur; yeni hedef + yeni parola: kabul.
        saved = await Json(await Send(admin, HttpMethod.Put, Settings, Smtp(version, "smtp.firma.test", 587, null)));
        Assert.True(saved.GetProperty("smtpSifreTanimli").GetBoolean());
        await Json(await Send(admin, HttpMethod.Put, Settings, Smtp(saved.GetProperty("surum").GetString(), "smtp2.firma.test", 587, "yeni-" + Random("p"))));
    }

    // #304 L2 — kayıtlı sır yalnız açık *Temizle bayrağıyla silinir; ""/null "koru" demektir; dolu değer bayraktan üstün.
    [Fact]
    public async Task Stored_secret_is_cleared_only_by_explicit_flag_and_audited()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var version = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();
        var saved = await Json(await Send(admin, HttpMethod.Put, Settings, new
        {
            smtpHost = "smtp.firma.test", smtpPort = 587, smtpKullanici = "mailer", smtpSifre = "s-" + Random("p"),
            smsApiKey = "k-" + Random("p"), posMerchantId = "m1", posApiKey = "pk-" + Random("p"),
            eFaturaKullanici = "ef", eFaturaSifre = "ef-" + Random("p"), surum = version,
        }));
        Task<SecretRow> Read() => _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking()
            .Select(x => new SecretRow(x.SmtpSifreEnc, x.SmsApiKeyEnc, x.PosApiKeyEnc, x.EFaturaSifreEnc)).FirstAsync());
        var before = await Read();
        Assert.All(new[] { before.Smtp, before.Sms, before.Pos, before.EFatura }, c => Assert.False(string.IsNullOrEmpty(c)));

        // "" ve null: dört sır da korunur (bayrak yok).
        saved = await Json(await Send(admin, HttpMethod.Put, Settings, new
        {
            smtpHost = "smtp.firma.test", smtpPort = 587, smtpKullanici = "mailer", posMerchantId = "m1", eFaturaKullanici = "ef",
            smtpSifre = "", smsApiKey = (string?)null, posApiKey = "  ", eFaturaSifre = "",
            surum = saved.GetProperty("surum").GetString(),
        }));
        Assert.Equal(before, await Read());

        // Bayrak: yalnız işaretlenen sır silinir, diğerleri aynen kalır; yanıt "tanımlı değil" der.
        saved = await Json(await Send(admin, HttpMethod.Put, Settings, new
        {
            smtpHost = "smtp.firma.test", smtpPort = 587, smtpKullanici = "mailer", posMerchantId = "m1", eFaturaKullanici = "ef",
            smtpSifreTemizle = true, smsApiKeyTemizle = true, surum = saved.GetProperty("surum").GetString(),
        }));
        var cleared = await Read();
        Assert.Null(cleared.Smtp);
        Assert.Null(cleared.Sms);
        Assert.Equal(before.Pos, cleared.Pos);
        Assert.Equal(before.EFatura, cleared.EFatura);
        Assert.False(saved.GetProperty("smtpSifreTanimli").GetBoolean());
        Assert.False(saved.GetProperty("smsApiKeyTanimli").GetBoolean());
        Assert.True(saved.GetProperty("posApiKeyTanimli").GetBoolean());

        // Denetim izi: silme, "Ayarlar" tablosunda (TenantSettings) bir Update kaydıdır; yeni değer null.
        var audits = (await _kit.ReadAsync(e.TenantId, db => db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityName == "Ayarlar" && a.Action == RentACar.Domain.Enums.AuditAction.Update && a.NewValues != null).Select(a => a.NewValues!).ToListAsync()))
            .Select(v => System.Text.Json.JsonDocument.Parse(v).RootElement)
            .Where(j => j.TryGetProperty("SmtpSifreEnc", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Null)
            .ToList();
        var audit = Assert.Single(audits);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, audit.GetProperty("SmsApiKeyEnc").ValueKind);

        // Dolu değer bayraktan üstün: POS anahtarı hem bayrak hem yeni değerle gelirse yeni değer yazılır.
        var newKey = "yeni-" + Random("p");
        await Json(await Send(admin, HttpMethod.Put, Settings, new
        {
            smtpHost = "smtp.firma.test", smtpPort = 587, smtpKullanici = "mailer", posMerchantId = "m1", eFaturaKullanici = "ef",
            posApiKey = newKey, posApiKeyTemizle = true, eFaturaSifreTemizle = true, surum = saved.GetProperty("surum").GetString(),
        }));
        var last = await Read();
        Assert.Equal(newKey, fx.Web.Services.GetRequiredService<RentACar.Application.Common.ISecretProtector>().Unprotect(last.Pos));
        Assert.Null(last.EFatura);
    }

    private sealed record SecretRow(string? Smtp, string? Sms, string? Pos, string? EFatura);

    // M4 — test gönderim uçları hız sınırı politikası taşır (test host'unda sınır yüksek; kapı metadata'dan kilitlenir).
    [Fact]
    public void Send_test_endpoints_are_rate_limited()
    {
        // 3. tur: girişten AYRI kova (test gönderimleri, alan adı ekle/doğrula). F13.1a: Blazor karşılıkları
        // (/ayarlar/smtp-test|sms-test|whatsapp-test|domain-ekle) silindi; yalnız API uçları kalır.
        string[] expected =
        [
            "/api/ui/v1/ayarlar/test/eposta", "/api/ui/v1/ayarlar/test/sms", "/api/ui/v1/ayarlar/test/whatsapp",
            "/api/ui/v1/ayarlar/domainler", "/api/ui/v1/ayarlar/domainler/dogrula",
        ];
        var routes = fx.Web.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(x => x.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()?.HttpMethods.Contains("POST") == true
                        && expected.Contains("/" + x.RoutePattern.RawText?.TrimStart('/')))
            .ToList();
        Assert.Equal(expected.Length, routes.Count);
        Assert.All(routes, r => Assert.Equal(RentACar.Web.Api.Sistem.SystemAdminApi.ExternalActionRatePolicy,
            r.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName));
    }

    // M4 — SMTP portu yalnız SMTP portları; test gönderimi iç ağa bağlanmaz, ham hata metni dönmez.
    [Fact]
    public async Task Smtp_port_is_restricted_and_internal_targets_fail_generically()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var version = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();
        await Problem(await Send(admin, HttpMethod.Put, Settings, Smtp(version, "smtp.firma.test", 6379, "p-" + Random("p"))),
            HttpStatusCode.BadRequest, "dogrulama", "smtpPort");

        var saved = await Json(await Send(admin, HttpMethod.Put, Settings, new
        {
            smtpHost = "169.254.169.254", smtpPort = 587, smtpKullanici = "mailer", smtpSifre = "p-" + Random("p"),
            smtpGonderenAdres = "gonderen@firma.test", surum = version,
        }));
        Assert.True(saved.GetProperty("smtpSifreTanimli").GetBoolean());
        var r = await Json(await Send(admin, HttpMethod.Post, Settings + "/test/eposta", new { alici = "alici@firma.test" }));
        Assert.False(r.GetProperty("basarili").GetBoolean());
        Assert.Equal(RentACar.Infrastructure.Integrations.MailKitEmailSender.GenericFailure, r.GetProperty("mesaj").GetString());
    }
}
