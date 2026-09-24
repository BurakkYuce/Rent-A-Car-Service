using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

/// <summary>
/// F11.1b — <c>/api/ui/v1/{ayarlar,kullanicilar,yetki,denetim,mesaj-sablonlari,bildirimler,ara,profil}</c> GERÇEK Web boru
/// hattında. BAĞIMSIZ ORACLE: sır değerleri test içinde rastgele üretilir; beklenen bayrak/durum/hata alanları elle
/// kurulmuş senaryodan yazılır (servis kodundan türetilmez).
/// </summary>
[Collection("web")]
public sealed partial class UiSystemAdminTests(WebFixture fx)
{
    private readonly SystemApiTestKit _kit = new(fx);

    private const string Settings = V1 + "/ayarlar";

    private static string Secret() => "S" + Guid.NewGuid().ToString("N");

    private static object SettingsBody(string? surum, string? smtpSifre = null, string? smsApiKey = null,
        string? eFaturaSifre = null, string? posApiKey = null, string? faturaSeriKodu = "RNT", string? logoUrl = null,
        Guid? grupId = null, string firmaUnvan = "Deneme A.Ş.")
        => new
        {
            firmaUnvan, firmaEmail = "info@deneme.test", smtpHost = "smtp.deneme.test", smtpPort = 587, smtpKullanici = "mailer",
            smtpSifre, smsApiKey, eFaturaSifre, posApiKey, smsBaslik = "DENEME", faturaSeriKodu, logoUrl,
            varsayilanKdvOrani = 0.20m, varsayilanGrupId = grupId, surum,
        };

    [Fact]
    public async Task Secrets_are_write_only_and_blank_keeps_existing_value()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var first = await Json(await admin.C.GetAsync(Settings));
        var surum = first.GetProperty("surum").GetString();
        Assert.False(first.GetProperty("smtpSifreTanimli").GetBoolean());

        var (smtp, sms, efatura, pos) = (Secret(), Secret(), Secret(), Secret());
        var r = await Send(admin, HttpMethod.Put, Settings, SettingsBody(surum, smtp, sms, efatura, pos));
        var text = await r.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        foreach (var s in new[] { smtp, sms, efatura, pos }) Assert.DoesNotContain(s, text, StringComparison.Ordinal);
        var saved = JsonDocument.Parse(text).RootElement;
        Assert.True(saved.GetProperty("smtpSifreTanimli").GetBoolean());
        Assert.True(saved.GetProperty("smsApiKeyTanimli").GetBoolean());
        Assert.True(saved.GetProperty("eFaturaSifreTanimli").GetBoolean());
        Assert.True(saved.GetProperty("posApiKeyTanimli").GetBoolean());
        Assert.False(saved.TryGetProperty("smtpSifre", out _));
        Assert.Equal("Deneme A.Ş.", saved.GetProperty("firmaUnvan").GetString());

        var cipher = await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => x.SmtpSifreEnc).FirstAsync());
        Assert.False(string.IsNullOrEmpty(cipher));
        Assert.NotEqual(smtp, cipher);

        // Boş sır → mevcut cipher korunur; diğer alan güncellenir.
        r = await Send(admin, HttpMethod.Put, Settings, SettingsBody(saved.GetProperty("surum").GetString(), firmaUnvan: "Yeni Ünvan"));
        var again = await Json(r);
        Assert.True(again.GetProperty("smtpSifreTanimli").GetBoolean());
        Assert.Equal("Yeni Ünvan", again.GetProperty("firmaUnvan").GetString());
        Assert.Equal(cipher, await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => x.SmtpSifreEnc).FirstAsync()));

        // SIR TARAMASI: düz değer de cipher de hiçbir GET yanıtında yok (denetim izi dahil — interceptor cipher'ı yazar).
        foreach (var url in new[] { Settings, V1 + "/kullanicilar", V1 + "/denetim?boyut=200", V1 + "/denetim?tablo=Ayarlar",
                                    V1 + "/mesaj-sablonlari", V1 + "/yetki/ekranlar", V1 + "/bildirimler" })
        {
            var body = await (await admin.C.GetAsync(url)).Content.ReadAsStringAsync();
            foreach (var s in new[] { smtp, sms, efatura, pos, cipher! })
                Assert.False(body.Contains(s, StringComparison.Ordinal), $"{url} sır sızdırıyor");
        }
        var audit = await Json(await admin.C.GetAsync(V1 + "/denetim?tablo=Ayarlar&boyut=200"));
        Assert.Contains(audit.GetProperty("kayitlar").EnumerateArray(),
            a => a.GetProperty("yeniDegerler").GetString() is { } v && v.Contains("\"SmtpSifreEnc\":\"***\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Settings_put_requires_version_and_rejects_stale_version()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var surum = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();
        Assert.NotNull(surum);

        await Problem(await Send(admin, HttpMethod.Put, Settings, SettingsBody(null)), HttpStatusCode.BadRequest, "dogrulama", "surum");
        await Json(await Send(admin, HttpMethod.Put, Settings, SettingsBody(surum)));
        // Aynı (artık bayat) sürümle ikinci yazım: 409, hiçbir şey yazılmaz.
        await Problem(await Send(admin, HttpMethod.Put, Settings, SettingsBody(surum, firmaUnvan: "Ezilmemeli")), HttpStatusCode.Conflict, "cakisma");
        Assert.Equal("Deneme A.Ş.", await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => x.FirmaUnvan).FirstAsync()));
    }

    [Fact]
    public async Task Settings_validation_maps_to_fields()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var surum = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();
        await Problem(await Send(admin, HttpMethod.Put, Settings, SettingsBody(surum, faturaSeriKodu: "AB")), HttpStatusCode.BadRequest, "dogrulama", "faturaSeriKodu");
        await Problem(await Send(admin, HttpMethod.Put, Settings, SettingsBody(surum, logoUrl: "javascript:alert(1)")), HttpStatusCode.BadRequest, "dogrulama", "logoUrl");
        await Problem(await Send(admin, HttpMethod.Put, Settings, SettingsBody(surum, grupId: Guid.NewGuid())), HttpStatusCode.BadRequest, "dogrulama", "varsayilanGrupId");
        await Problem(await Send(admin, HttpMethod.Put, Settings, SettingsBody(surum, smtpSifre: new string('x', 300))), HttpStatusCode.BadRequest, "dogrulama", "smtpSifre");
    }

    [Theory]
    [InlineData(Who.Manager, "/ayarlar")]
    [InlineData(Who.Manager, "/kullanicilar")]
    [InlineData(Who.OperatorA, "/yetki/ekranlar")]
    [InlineData(Who.Accounting, "/denetim")]
    [InlineData(Who.OperatorA, "/mesaj-sablonlari")]
    [InlineData(Who.Manager, "/yetki/matris")]
    public async Task System_admin_endpoints_require_manage_users(Who who, string path)
    {
        var e = await _kit.SetupAsync();
        var s = await _kit.LoginAsync(e, who);
        await Problem(await s.C.GetAsync(V1 + path), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(s, HttpMethod.Put, Settings, SettingsBody("1")), HttpStatusCode.Forbidden, "yetki_yok");
    }

    [Fact]
    public async Task Send_tests_are_honest_without_configuration()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        foreach (var (path, body) in new (string, object)[]
                 {
                     ("/test/sms", new { telefon = "+905321112233" }),
                     ("/test/whatsapp", new { telefon = "+905321112233" }),
                     ("/test/eposta", new { alici = "alici@deneme.test" }),
                 })
        {
            var r = await Json(await Send(admin, HttpMethod.Post, Settings + path, body));
            Assert.False(r.GetProperty("basarili").GetBoolean(), $"{path} yapılandırmasız başarı dönmemeli");
            Assert.NotEqual("gonderildi", r.GetProperty("durum").GetString());
        }
        await Problem(await Send(admin, HttpMethod.Post, Settings + "/test/sms", new { telefon = "0532" }), HttpStatusCode.BadRequest, "dogrulama", "telefon");
        await Problem(await Send(admin, HttpMethod.Post, Settings + "/test/eposta", new { alici = "yok" }), HttpStatusCode.BadRequest, "dogrulama", "alici");
    }

    [Theory]
    [InlineData("baska.rentpro.com")]
    [InlineData("rentpro.com")]
    [InlineData("127.0.0.1")]
    [InlineData("http://site.com/yol")]
    [InlineData("tekkelime")]
    public async Task Custom_domain_rejects_platform_space_and_malformed_hosts(string host)
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        await Problem(await Send(admin, HttpMethod.Post, Settings + "/domainler", new { host }), HttpStatusCode.BadRequest, "dogrulama", "host");
    }

    [Fact]
    public async Task Custom_domain_valid_host_is_pending()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var host = "www." + Random("f") + ".com.tr";
        var r = await Json(await Send(admin, HttpMethod.Post, Settings + "/domainler", new { host = host.ToUpperInvariant() }));
        Assert.Contains(r.GetProperty("domainler").EnumerateArray(), d => d.GetProperty("host").GetString() == host);
    }

    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static MultipartFormDataContent File(byte[] bytes, string name, string type)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { content, "dosya", name } };
    }

    [Fact]
    public async Task Logo_upload_detects_type_from_content_and_serves_with_nosniff()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        // Uzantı ve Content-Type PNG diyor, içerik HTML: reddedilir.
        await Problem(await Send(admin, HttpMethod.Post, Settings + "/logo",
            File("<html><script>alert(1)</script></html>"u8.ToArray(), "logo.png", "image/png")), HttpStatusCode.BadRequest, "dogrulama", "dosya");
        await Problem(await admin.C.GetAsync(Settings + "/logo"), HttpStatusCode.NotFound, null);

        var png = Convert.FromBase64String(OnePixelPng);
        var up = await Json(await Send(admin, HttpMethod.Post, Settings + "/logo", File(png, "x.bin", "application/octet-stream")));
        Assert.Equal(png.Length, up.GetProperty("bayt").GetInt32());
        var get = await admin.C.GetAsync(Settings + "/logo");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
        Assert.Contains("nosniff", get.Headers.GetValues("X-Content-Type-Options"));
        Assert.Null(get.Content.Headers.ContentDisposition);
        Assert.Equal(png, await get.Content.ReadAsByteArrayAsync());
        Assert.True((await Json(await admin.C.GetAsync(Settings))).GetProperty("logoVar").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Delete, Settings + "/logo")).StatusCode);
        await Problem(await admin.C.GetAsync(Settings + "/logo"), HttpStatusCode.NotFound, null);
    }
}
