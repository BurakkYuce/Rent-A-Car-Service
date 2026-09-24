using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiSystemSecurityTests
{
    private const string Settings = V1 + "/ayarlar";

    private static object Smtp(string? surum, string host, int port, string? password)
        => new { smtpHost = host, smtpPort = port, smtpKullanici = "mailer", smtpSifre = password, surum };

    // M3 — SMTP sunucusu değiştirilip parola boş bırakılarak kayıtlı parola saldırgan sunucuya gönderilemez.
    [Fact]
    public async Task Changing_smtp_target_requires_reentering_the_password()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var surum = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();
        var saved = await Json(await Send(admin, HttpMethod.Put, Settings, Smtp(surum, "smtp.firma.test", 587, "ilk-" + Random("p"))));
        var cipher = await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => x.SmtpSifreEnc).FirstAsync());
        surum = saved.GetProperty("surum").GetString();

        await Problem(await Send(admin, HttpMethod.Put, Settings, Smtp(surum, "smtp.saldirgan.test", 587, null)),
            HttpStatusCode.BadRequest, "dogrulama", "smtpSifre");
        await Problem(await Send(admin, HttpMethod.Put, Settings, Smtp(surum, "smtp.firma.test", 465, null)),
            HttpStatusCode.BadRequest, "dogrulama", "smtpSifre");
        var after = await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => new { x.SmtpHost, x.SmtpSifreEnc }).FirstAsync());
        Assert.Equal("smtp.firma.test", after.SmtpHost);
        Assert.Equal(cipher, after.SmtpSifreEnc);

        // Aynı hedef + boş parola: korunur; yeni hedef + yeni parola: kabul.
        saved = await Json(await Send(admin, HttpMethod.Put, Settings, Smtp(surum, "smtp.firma.test", 587, null)));
        Assert.True(saved.GetProperty("smtpSifreTanimli").GetBoolean());
        await Json(await Send(admin, HttpMethod.Put, Settings, Smtp(saved.GetProperty("surum").GetString(), "smtp2.firma.test", 587, "yeni-" + Random("p"))));
    }

    // M4 — test gönderim uçları hız sınırı politikası taşır (test host'unda sınır yüksek; kapı metadata'dan kilitlenir).
    [Fact]
    public void Send_test_endpoints_are_rate_limited()
    {
        var routes = fx.Web.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(x => x.RoutePattern.RawText?.Contains("/ayarlar/test/", StringComparison.Ordinal) == true)
            .ToList();
        Assert.Equal(3, routes.Count);
        Assert.All(routes, r => Assert.Equal("login",
            r.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName));
    }

    // M4 — SMTP portu yalnız SMTP portları; test gönderimi iç ağa bağlanmaz, ham hata metni dönmez.
    [Fact]
    public async Task Smtp_port_is_restricted_and_internal_targets_fail_generically()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var surum = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();
        await Problem(await Send(admin, HttpMethod.Put, Settings, Smtp(surum, "smtp.firma.test", 6379, "p-" + Random("p"))),
            HttpStatusCode.BadRequest, "dogrulama", "smtpPort");

        var saved = await Json(await Send(admin, HttpMethod.Put, Settings, new
        {
            smtpHost = "169.254.169.254", smtpPort = 587, smtpKullanici = "mailer", smtpSifre = "p-" + Random("p"),
            smtpGonderenAdres = "gonderen@firma.test", surum,
        }));
        Assert.True(saved.GetProperty("smtpSifreTanimli").GetBoolean());
        var r = await Json(await Send(admin, HttpMethod.Post, Settings + "/test/eposta", new { alici = "alici@firma.test" }));
        Assert.False(r.GetProperty("basarili").GetBoolean());
        Assert.Equal(RentACar.Infrastructure.Integrations.MailKitEmailSender.GenericFailure, r.GetProperty("mesaj").GetString());
    }
}
