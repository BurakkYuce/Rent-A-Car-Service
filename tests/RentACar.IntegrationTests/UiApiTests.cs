using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Api;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

/// <summary>
/// F1.2 — <c>/api/ui/v1</c> iskeleti GERÇEK Web boru hattında: 401/403/400/409/429 ProblemDetails
/// (yönlendirme/HTML değil), CSRF (başlıksız ve girişten önce alınmış belirteç reddi), oturum uçları,
/// pilot kapısı, kapalı firma, no-store ve Blazor davranışının değişmediği.
/// BAĞIMSIZ ORACLE: beklenen kod/durum değerleri elle yazılmış sabitlerdir (UiHata sabitleri kullanılmaz).
/// </summary>
[Collection("web")]
public sealed class UiApiTests(WebFixture fx)
{
    private const string Ben = "/api/ui/v1/oturum/ben";
    private const string Giris = "/api/ui/v1/oturum/giris";
    private const string Cikis = "/api/ui/v1/oturum/cikis";
    private const string Xsrf = "/api/ui/v1/oturum/xsrf";

    // ------------------------------------------------------------ yardımcılar

    private static string? CerezDegeri(HttpResponseMessage r, string ad)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var degerler)) return null;
        foreach (var d in degerler)
            if (d.StartsWith(ad + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(ad.Length + 1)..].Split(';')[0]);
        return null;
    }

    private static async Task<JsonElement> Govde(HttpResponseMessage r)
        => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task ProblemBekle(HttpResponseMessage r, HttpStatusCode durum, string? kod)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(durum == r.StatusCode, $"Beklenen {(int)durum}, gelen {(int)r.StatusCode}: {metin}");
        Assert.Null(r.Headers.Location); // yönlendirme YOK
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var j = JsonDocument.Parse(metin).RootElement;
        Assert.Equal((int)durum, j.GetProperty("status").GetInt32());
        Assert.True(j.TryGetProperty("title", out _));
        if (kod is null) Assert.False(j.TryGetProperty("kod", out _));
        else Assert.Equal(kod, j.GetProperty("kod").GetString());
        Assert.True(r.Headers.CacheControl?.NoStore == true, "no-store eksik");
    }

    private static HttpRequestMessage Istek(HttpMethod m, string url, string? xsrf, object? govde = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (xsrf is not null) req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (govde is not null) req.Content = JsonContent.Create(govde);
        return req;
    }

    private static async Task<string> XsrfAl(HttpClient c)
    {
        var r = await c.GetAsync(Xsrf);
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        return CerezDegeri(r, "XSRF-TOKEN") ?? throw new Xunit.Sdk.XunitException("XSRF-TOKEN çerezi verilmedi");
    }

    /// <summary>JSON giriş gövdesi — kimlik fixture'da çalışma anında üretilir (sabit parola yok).</summary>
    private static object GirisGovdesi(TestKimlik k, string? sifre = null)
        => new { firma = k.Firma, kullanici = k.Kullanici, sifre = sifre ?? k.Sifre };

    /// <summary>Giriş yapar; (istemci, girişten ÖNCEKİ belirteç, girişten SONRAKİ belirteç) döner.</summary>
    private async Task<(HttpClient C, string Once, string Sonra)> GirisYap(TestKimlik? k = null, WebFactory? f = null)
    {
        var c = (f ?? fx.Web).Istemci();
        var once = await XsrfAl(c);
        var r = await c.SendAsync(Istek(HttpMethod.Post, Giris, once, GirisGovdesi(k ?? fx.PilotAdmin)));
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var sonra = CerezDegeri(r, "XSRF-TOKEN") ?? throw new Xunit.Sdk.XunitException("girişte XSRF yenilenmedi");
        return (c, once, sonra);
    }

    // ------------------------------------------------------------ 401 / oturum

    [Fact]
    public async Task Anonim_ben_401_oturum_yok_json_302_degil()
    {
        var r = await fx.Web.Istemci().GetAsync(Ben);
        await ProblemBekle(r, HttpStatusCode.Unauthorized, "oturum_yok");
    }

    [Fact]
    public async Task Giris_oturum_cerezi_ve_taze_xsrf_verir_ben_doner()
    {
        var c = fx.Web.Istemci();
        var once = await XsrfAl(c);
        var r = await c.SendAsync(Istek(HttpMethod.Post, Giris, once, GirisGovdesi(fx.PilotAdmin)));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.NotNull(CerezDegeri(r, "racar.session"));
        var sonra = CerezDegeri(r, "XSRF-TOKEN");
        Assert.NotNull(sonra);
        Assert.NotEqual(once, sonra);
        var xsrfCerez = r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        Assert.Contains("path=/", xsrfCerez, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", xsrfCerez, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", xsrfCerez, StringComparison.OrdinalIgnoreCase); // JS okuyabilmeli
        Assert.True(r.Headers.CacheControl?.NoStore == true);

        var j = await Govde(r);
        Assert.Equal(fx.PilotAdmin.Kullanici, j.GetProperty("kullanici").GetProperty("kullaniciAdi").GetString());
        Assert.Equal("Test Admin", j.GetProperty("kullanici").GetProperty("adSoyad").GetString());
        Assert.Equal(fx.PilotAdmin.Firma, j.GetProperty("kiraci").GetProperty("kod").GetString());
        Assert.Equal("Pilot Test Firması", j.GetProperty("kiraci").GetProperty("ad").GetString());
        Assert.Equal(fx.PilotFirmaId, j.GetProperty("kiraci").GetProperty("id").GetGuid());
        Assert.Equal("Admin", j.GetProperty("rol").GetString());
        var izinler = j.GetProperty("izinler").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("ManageUsers", izinler);
        Assert.Contains("FinanceWrite", izinler);
        Assert.True(j.GetProperty("subeKapsami").GetProperty("tumSubeler").GetBoolean());
        Assert.False(j.GetProperty("moduller").GetProperty("webSitesi").GetBoolean());
        Assert.True(j.GetProperty("pilot").GetBoolean());

        var ben = await c.GetAsync(Ben);
        Assert.Equal(HttpStatusCode.OK, ben.StatusCode);
        Assert.True(ben.Headers.CacheControl?.NoStore == true);
    }

    [Fact]
    public async Task Operator_ben_sube_kapsami_ve_dar_izinler()
    {
        var (c, _, _) = await GirisYap(fx.PilotOperator);
        var j = await Govde(await c.GetAsync(Ben));

        Assert.Equal("Operator", j.GetProperty("rol").GetString());
        var kapsam = j.GetProperty("subeKapsami");
        Assert.False(kapsam.GetProperty("tumSubeler").GetBoolean());
        Assert.Equal("Merkez", kapsam.GetProperty("subeAd").GetString());
        var izinler = j.GetProperty("izinler").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("OperationsWrite", izinler);
        Assert.DoesNotContain("FinanceWrite", izinler);
        Assert.DoesNotContain("ManageUsers", izinler);
    }

    [Fact]
    public async Task Hatali_sifre_400_dogrulama_oturum_acilmaz()
    {
        var c = fx.Web.Istemci();
        var t = await XsrfAl(c);
        var r = await c.SendAsync(Istek(HttpMethod.Post, Giris, t, GirisGovdesi(fx.PilotAdmin, WebFixture.RastgeleParola())));

        await ProblemBekle(r, HttpStatusCode.BadRequest, "dogrulama");
        Assert.Null(CerezDegeri(r, "racar.session"));
        await ProblemBekle(await c.GetAsync(Ben), HttpStatusCode.Unauthorized, "oturum_yok");
    }

    [Fact]
    public async Task Cikis_oturumu_kapatir_belirteci_yeniler()
    {
        var (c, _, sonra) = await GirisYap();

        var r = await c.SendAsync(Istek(HttpMethod.Post, Cikis, sonra));
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var cikisSonrasi = CerezDegeri(r, "XSRF-TOKEN");
        Assert.NotNull(cikisSonrasi);
        await ProblemBekle(await c.GetAsync(Ben), HttpStatusCode.Unauthorized, "oturum_yok");

        // Kullanıcıya bağlı eski belirteç anonim kimlikte reddedilir; çıkışta verilen kabul edilir.
        var eski = await c.SendAsync(Istek(HttpMethod.Post, Giris, sonra, GirisGovdesi(fx.PilotAdmin)));
        await ProblemBekle(eski, HttpStatusCode.BadRequest, "xsrf_gecersiz");
        var yeni = await c.SendAsync(Istek(HttpMethod.Post, Giris, cikisSonrasi, GirisGovdesi(fx.PilotAdmin)));
        Assert.Equal(HttpStatusCode.OK, yeni.StatusCode);
    }

    // ------------------------------------------------------------ CSRF

    [Fact]
    public async Task Basliksiz_guvensiz_istek_reddedilir()
    {
        var (c, _, sonra) = await GirisYap();

        await ProblemBekle(await c.SendAsync(Istek(HttpMethod.Post, "/api/ui/v1/test/yaz", null)),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
        await ProblemBekle(await c.SendAsync(Istek(HttpMethod.Post, "/api/ui/v1/test/yaz", "uydurma-belirtec")),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Istek(HttpMethod.Post, "/api/ui/v1/test/yaz", sonra))).StatusCode);
        // Güvenli yöntem belirteç istemez.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/ui/v1/test/tamam")).StatusCode);
    }

    [Fact]
    public async Task Giristen_once_alinan_belirtec_giristen_sonra_reddedilir()
    {
        var (c, once, sonra) = await GirisYap();

        await ProblemBekle(await c.SendAsync(Istek(HttpMethod.Post, "/api/ui/v1/test/yaz", once)),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Istek(HttpMethod.Post, "/api/ui/v1/test/yaz", sonra))).StatusCode);
    }

    [Fact]
    public async Task Giris_de_csrf_ister()
    {
        var c = fx.Web.Istemci();
        await XsrfAl(c); // çerez var ama başlık yok
        var r = await c.SendAsync(Istek(HttpMethod.Post, Giris, null, GirisGovdesi(fx.PilotAdmin)));
        await ProblemBekle(r, HttpStatusCode.BadRequest, "xsrf_gecersiz");
        Assert.Null(CerezDegeri(r, "racar.session"));
    }

    [Fact]
    public async Task Baska_kullanicinin_belirteci_kabul_edilmez()
    {
        var (_, _, adminBelirteci) = await GirisYap();
        var (op, _, _) = await GirisYap(fx.PilotOperator);

        await ProblemBekle(await op.SendAsync(Istek(HttpMethod.Post, "/api/ui/v1/test/yaz", adminBelirteci)),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
    }

    // ------------------------------------------------------------ 403 / 400 / 409 / 500

    [Fact]
    public async Task Servis_YetkiYok_403_yetki_yok()
        => await ProblemBekle(await (await GirisYap()).C.GetAsync("/api/ui/v1/test/yetki-yok"),
            HttpStatusCode.Forbidden, "yetki_yok");

    [Fact]
    public async Task Uc_izin_kapisi_403_yetki_yok_yetkisiz_sayfasina_yonlendirmez()
    {
        var (c, _, _) = await GirisYap(fx.PilotOperator);
        await ProblemBekle(await c.GetAsync("/api/ui/v1/test/finans"), HttpStatusCode.Forbidden, "yetki_yok");

        var (admin, _, _) = await GirisYap();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/ui/v1/test/finans")).StatusCode);
    }

    [Fact]
    public async Task Pilot_olmayan_firma_403_pilot_degil_oturum_uclari_acik()
    {
        var (c, _, sonra) = await GirisYap(fx.DigerAdmin);

        await ProblemBekle(await c.GetAsync("/api/ui/v1/test/tamam"), HttpStatusCode.Forbidden, "pilot_degil");
        await ProblemBekle(await c.SendAsync(Istek(HttpMethod.Post, "/api/ui/v1/test/yaz", sonra)),
            HttpStatusCode.Forbidden, "pilot_degil");

        var ben = await c.GetAsync(Ben); // oturum/* pilot kapısından muaf
        Assert.Equal(HttpStatusCode.OK, ben.StatusCode);
        Assert.False((await Govde(ben)).GetProperty("pilot").GetBoolean());

        var (pilot, _, _) = await GirisYap();
        Assert.Equal(HttpStatusCode.OK, (await pilot.GetAsync("/api/ui/v1/test/tamam")).StatusCode);
    }

    [Fact]
    public async Task Pilot_bayragi_kapatilinca_aninda_kapanir()
    {
        var k = await fx.FirmaVeKullaniciAsync("Pilot Aç-Kapa Firması");
        var yeni = await fx.TenantIdAsync(k.Firma);
        await fx.PilotYapAsync(yeni, true);
        var (c, _, _) = await GirisYap(k);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/ui/v1/test/tamam")).StatusCode);

        await fx.PilotYapAsync(yeni, false);
        await ProblemBekle(await c.GetAsync("/api/ui/v1/test/tamam"), HttpStatusCode.Forbidden, "pilot_degil");
    }

    [Fact]
    public async Task Alanli_ValidationException_400_errors_tasir()
    {
        var (c, _, t) = await GirisYap();
        var r = await c.SendAsync(Istek(HttpMethod.Post, "/api/ui/v1/test/dogrulama", t));

        await ProblemBekle(r, HttpStatusCode.BadRequest, "dogrulama");
        var j = await Govde(r);
        Assert.Equal("Plaka zorunludur.", j.GetProperty("detail").GetString());
        Assert.Equal("Plaka zorunludur.", j.GetProperty("errors").GetProperty("Plaka")[0].GetString());
    }

    [Fact]
    public async Task Cakisma_ve_mukerrer_ayri_409_kodlari()
    {
        var (c, _, t) = await GirisYap();

        await ProblemBekle(await c.SendAsync(Istek(HttpMethod.Post, "/api/ui/v1/test/cakisma", t)),
            HttpStatusCode.Conflict, "cakisma");
        await ProblemBekle(await c.SendAsync(Istek(HttpMethod.Post, "/api/ui/v1/test/mukerrer", t)),
            HttpStatusCode.Conflict, "mukerrer");
    }

    [Fact]
    public async Task Beklenmeyen_hata_500_problem_ayrinti_sizdirmaz()
    {
        var r = await (await GirisYap()).C.GetAsync("/api/ui/v1/test/patla");

        await ProblemBekle(r, HttpStatusCode.InternalServerError, null);
        Assert.DoesNotContain(TestUiUclari.GizliAyrinti, await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Bozuk_json_400_500_degil()
    {
        var c = fx.Web.Istemci();
        var t = await XsrfAl(c);
        var req = Istek(HttpMethod.Post, Giris, t);
        req.Content = new StringContent("{bozuk", Encoding.UTF8, "application/json");
        var r = await c.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.Null(r.Headers.Location);
    }

    /// <summary>
    /// Low temizliği A: üretimde <c>ThrowOnBadRequest</c> KAPALI (varsayılanı yalnız Development'ta açık) — bağlama
    /// hatası istisna değil gövdesiz 400 olur. Aynı host üretim ayarıyla kurulur; yanıt yine <c>kod: dogrulama</c>.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Bozuk_json_her_ortamda_400_kod_dogrulama(bool gelistirmeGibi)
    {
        await using var f = fx.Web.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = gelistirmeGibi)));
        var c = f.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var t = await XsrfAl(c);
        var req = Istek(HttpMethod.Post, Giris, t);
        req.Content = new StringContent("{bozuk", Encoding.UTF8, "application/json");
        var r = await c.SendAsync(req);

        await ProblemBekle(r, HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal("İstek gövdesi okunamadı ya da eksik.", (await Govde(r)).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("22001", LogLevel.Warning)]
    [InlineData("22003", LogLevel.Warning)]
    [InlineData("23505", LogLevel.Error)]   // tanınmayan DB hatası: 500 + Error (gizlenmez)
    public void Veri_tasmasi_agi_Warning_loglanir(string sqlState, LogLevel beklenen)
    {
        var ex = new Microsoft.EntityFrameworkCore.DbUpdateException("x",
            new PostgresException("taşma", "ERROR", "ERROR", sqlState));
        Assert.Equal(beklenen, UiApiExtensions.LogSeviyesi(ex));
    }

    [Fact]
    public void Istemci_hatasi_Information_beklenmeyen_Error()
    {
        Assert.Equal(LogLevel.Information, UiApiExtensions.LogSeviyesi(new RentACar.Application.Common.ValidationException("x")));
        Assert.Equal(LogLevel.Information, UiApiExtensions.LogSeviyesi(new BadHttpRequestException("x")));
        Assert.Equal(LogLevel.Error, UiApiExtensions.LogSeviyesi(new InvalidOperationException("x")));
    }

    [Fact]
    public async Task Bilinmeyen_rota_ve_yanlis_yontem_json_html_degil()
    {
        var c = fx.Web.Istemci();
        await ProblemBekle(await c.GetAsync("/api/ui/v1/boyle-bir-uc-yok"), HttpStatusCode.NotFound, null);
        await ProblemBekle(await c.GetAsync("/api/ui/v1/test"), HttpStatusCode.NotFound, null);
        var yanlisYontem = await c.GetAsync(Giris); // POST-only uca GET: 404/405, ama her durumda JSON
        Assert.True(yanlisYontem.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        Assert.Equal("application/problem+json", yanlisYontem.Content.Headers.ContentType?.MediaType);
    }

    // ------------------------------------------------------------ kiracı kapalı / platform / hız sınırı

    [Fact]
    public async Task Kapali_firma_401_kiraci_kapali_ve_oturum_duser()
    {
        var k = await fx.FirmaVeKullaniciAsync("Kapanacak Firma");
        var id = await fx.TenantIdAsync(k.Firma);
        var (c, _, _) = await GirisYap(k);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Ben)).StatusCode);

        await using (var conn = new NpgsqlConnection(fx.Pg.OwnerConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("UPDATE \"Tenants\" SET \"IsActive\" = false WHERE \"Id\" = @i", conn);
            cmd.Parameters.AddWithValue("i", id);
            await cmd.ExecuteNonQueryAsync();
        }
        using (var scope = fx.Web.Services.CreateScope()) // platform konsolunun yaptığı gibi anında kesme
            scope.ServiceProvider.GetRequiredService<TenantStatusCache>().Invalidate(id);

        await ProblemBekle(await c.GetAsync(Ben), HttpStatusCode.Unauthorized, "kiraci_kapali");
        await ProblemBekle(await c.GetAsync(Ben), HttpStatusCode.Unauthorized, "oturum_yok"); // çerez silindi
    }

    [Fact]
    public async Task Platform_operatoru_ui_verisine_403_ben_401_yonlendirme_yok()
    {
        var c = fx.Web.Istemci();
        var giris = await c.PostAsync("/platform/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["kullanici"] = fx.Platform.Kullanici, ["sifre"] = fx.Platform.Sifre,
        }));
        Assert.Equal(HttpStatusCode.Redirect, giris.StatusCode);
        Assert.NotNull(CerezDegeri(giris, "racar.session"));

        await ProblemBekle(await c.GetAsync("/api/ui/v1/test/tamam"), HttpStatusCode.Forbidden, "yetki_yok");
        await ProblemBekle(await c.GetAsync(Ben), HttpStatusCode.Unauthorized, "oturum_yok"); // muaf ama firma oturumu yok
        Assert.Equal(HttpStatusCode.NoContent, (await c.GetAsync(Xsrf)).StatusCode);
    }

    [Fact]
    public async Task Giris_hiz_siniri_429_cok_istek_json()
    {
        var c = fx.DarLimitli.Istemci(); // limit 2
        var govde = GirisGovdesi(fx.PilotAdmin, WebFixture.RastgeleParola());
        await c.SendAsync(Istek(HttpMethod.Post, Giris, null, govde));
        await c.SendAsync(Istek(HttpMethod.Post, Giris, null, govde));
        await ProblemBekle(await c.SendAsync(Istek(HttpMethod.Post, Giris, null, govde)),
            HttpStatusCode.TooManyRequests, "cok_istek");
    }

    // ------------------------------------------------------------ Blazor değişmedi

    [Fact]
    public async Task Blazor_yonlendirmeleri_degismedi()
    {
        var c = fx.Web.Istemci();

        var ana = await c.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, ana.StatusCode);
        Assert.StartsWith("/login", ana.Headers.Location?.OriginalString);

        var cikis = await c.PostAsync("/auth/logout", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Redirect, cikis.StatusCode);
        Assert.Equal("/login", cikis.Headers.Location?.OriginalString);

        // Blazor girişi hâlâ form + yönlendirme; aynı claim setiyle /api/ui/ben'i de açar (tek cookie şeması).
        var giris = await c.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["firma"] = fx.PilotAdmin.Firma, ["kullanici"] = fx.PilotAdmin.Kullanici, ["sifre"] = fx.PilotAdmin.Sifre,
        }));
        Assert.Equal(HttpStatusCode.Redirect, giris.StatusCode);
        Assert.Equal("/", giris.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Ben)).StatusCode);

        // Bilinmeyen Blazor yolu hâlâ StatusCodePages'in not-found sayfası (ProblemDetails değil).
        var yok = await c.GetAsync("/boyle-bir-sayfa-yok");
        Assert.Equal(HttpStatusCode.NotFound, yok.StatusCode);
        Assert.Equal("text/html", yok.Content.Headers.ContentType?.MediaType);
    }
}

/// <summary>
/// F1.2 yapısal çit (EndpointDataSource — gerçek host'taki uçlar): her <c>/api/ui</c> ucu grupta eşlenmiş
/// (CSRF + pilot filtreleri), izin kapısı ya da AÇIK muafiyet taşıyor, modül yolundaki uç modül metadatası taşıyor.
/// </summary>
[Collection("web")]
public sealed class UiApiYapisalTests(WebFixture fx)
{
    /// <summary>Modül yolu → modül. Yeni satın alınabilir modülün uçları eklenirken buraya satır eklenir.</summary>
    private static readonly Dictionary<string, string> ModulYollari = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/api/ui/v1/web-sitesi"] = "WebSitesi",
    };

    private List<RouteEndpoint> UiUclari()
        => fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => ("/" + (e.RoutePattern.RawText ?? "").TrimStart('/')).StartsWith("/api/ui", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Rota(RouteEndpoint e) => "/" + (e.RoutePattern.RawText ?? "").TrimStart('/');

    [Fact]
    public void Oturum_uclari_kayitli()
    {
        var rotalar = UiUclari().Select(Rota).ToList();
        foreach (var r in new[] { "/api/ui/v1/oturum/giris", "/api/ui/v1/oturum/cikis", "/api/ui/v1/oturum/ben", "/api/ui/v1/oturum/xsrf" })
            Assert.Contains(r, rotalar);
    }

    [Fact]
    public void Her_ui_ucu_grupta_ve_izinli_ya_da_acikca_muaf()
    {
        var uclar = UiUclari();
        Assert.NotEmpty(uclar);
        var ihlal = uclar
            .Where(e => e.Metadata.GetMetadata<UiApiGrubuMetadata>() is null
                        || (e.Metadata.GetMetadata<IzinMetadata>() is null
                            && e.Metadata.GetMetadata<IzinlerdenBiriMetadata>() is null // F4.1: "izinlerden biri" kapısı
                            && e.Metadata.GetMetadata<IzinMuafMetadata>() is null))
            .Select(Rota).ToList();
        Assert.True(ihlal.Count == 0, "Grupsuz ya da izinsiz /api/ui ucu: " + string.Join(", ", ihlal));
    }

    [Fact]
    public void Her_ui_ucu_TypedResults_ile_yanit_turu_bildirir()
    {
        // TypedResults zorunlu. Ayrıca yakalar: yalnız (HttpContext) alıp Task<T> dönen yöntem grubu
        // RequestDelegate aşırı yüklemesine bağlanır, dönüş değeri yok sayılır (200 boş gövde) — o uçta
        // yanıt türü metadatası OLMAZ.
        var ihlal = UiUclari()
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>() is null)
            .Select(Rota).ToList();
        Assert.True(ihlal.Count == 0, "Yanıt türü bildirmeyen /api/ui ucu: " + string.Join(", ", ihlal));
    }

    [Fact]
    public void Muafiyet_yalniz_oturum_uclarinda()
    {
        // F1.6: /menu da muaf — her oturumun bir menüsü var, kapı ÖĞE düzeyinde (MenuApi.Gorunur).
        // F3.5: /tablo-duzenleri/* muaf — kişisel arayüz tercihi; kullanıcı oturumdan gelir (uçta kullanıcı
        // parametresi yok), iş verisi taşımaz. Kullanıcı/firma izolasyonu TabloDuzeniTests'te kilitli.
        var muaf = UiUclari().Where(e => e.Metadata.GetMetadata<IzinMuafMetadata>() is not null
                                         && e.Metadata.GetMetadata<IzinMetadata>() is null
                                         && e.Metadata.GetMetadata<IzinlerdenBiriMetadata>() is null)
            .Select(Rota).ToList();
        // F3.3: /istemci-hata da muaf — her oturum yalnız KENDİ tarayıcı hatasını raporlar (veri yok, yalnız log).
        Assert.All(muaf, r => Assert.True(r.StartsWith("/api/ui/v1/oturum/", StringComparison.Ordinal)
                                          || r == "/api/ui/v1/menu"
                                          || r == "/api/ui/v1/istemci-hata"
                                          // F4.1: ana ekran her oturumun; kapılar İÇERİKTE (finans ViewReports, tahsilat
                                          // anahtarı FinanceWrite) — UiKiraPanelTests içerik kapılarını kilitler.
                                          || r == "/api/ui/v1/panel/ozet"
                                          // F11.1b: Blazor'da yalnız [Authorize] olan kişisel/kiracı-geneli yüzeyler.
                                          // /profil/sifre — herkes KENDİ parolasını değiştirir; kimlik ICurrentUser'dan,
                                          // eski parola doğrulanır, giriş hız sınırı (UiSystemAdminTests.Password_*).
                                          || r == "/api/ui/v1/profil/sifre"
                                          // /ara — genel arama; şube kapsamı SearchService'te (BranchScope), kiracı RLS.
                                          || r == "/api/ui/v1/ara"
                                          // /bildirimler* — kiracının vade/şikayet bildirimleri; RLS izole, başka firmanın
                                          // bildirimi 404 (UiSystemAdminTests.Notifications_and_search_*).
                                          || r.StartsWith("/api/ui/v1/bildirimler", StringComparison.Ordinal)
                                          // F12.1: platform konsolu — firma izin matrisi yerine PlatformAdmin policy'si
                                          // (aşağıdaki Platform_uclari_platform_policy_tasir kilitler).
                                          || r.StartsWith("/api/ui/v1/platform/", StringComparison.Ordinal)
                                          || r.StartsWith("/api/ui/v1/tablo-duzenleri/", StringComparison.Ordinal), r));
    }

    [Fact]
    public void Platform_endpoints_carry_the_platform_policy_and_only_login_logout_are_anonymous()
    {
        // F12.1: every /api/ui/v1/platform endpoint is behind "PlatformAdmin"; the only anonymous ones are
        // login/logout. A new platform endpoint without the policy would be reachable by any tenant user
        // (IzinMuaf skips the tenant permission matrix) — this is the fence.
        var platform = UiUclari().Where(e => Rota(e).StartsWith("/api/ui/v1/platform/", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(platform);
        var anonymous = platform.Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null)
            .Select(Rota).OrderBy(r => r, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "/api/ui/v1/platform/oturum/cikis", "/api/ui/v1/platform/oturum/giris" }, anonymous);
        var missing = platform
            .Where(e => !e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                .Any(a => a.Policy == "PlatformAdmin"))
            .Select(Rota).ToList();
        Assert.True(missing.Count == 0, "PlatformAdmin policy eksik: " + string.Join(", ", missing));
        // And the policy is not used outside the platform area of the UI API.
        var leaked = UiUclari().Where(e => !Rota(e).StartsWith("/api/ui/v1/platform/", StringComparison.Ordinal)
                                           && e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                                               .Any(a => a.Policy == "PlatformAdmin"))
            .Select(Rota).ToList();
        Assert.Empty(leaked);
    }

    [Fact]
    public void Modul_yolundaki_uc_modul_metadatasi_tasir()
    {
        var ihlal = new List<string>();
        foreach (var e in UiUclari())
        foreach (var (onek, modul) in ModulYollari)
            if (Rota(e).StartsWith(onek + "/", StringComparison.OrdinalIgnoreCase)
                && e.Metadata.GetOrderedMetadata<ModulMetadata>().All(m => m.Modul != modul))
                ihlal.Add(Rota(e));
        Assert.True(ihlal.Count == 0, "Modül metadatası eksik: " + string.Join(", ", ihlal));
    }

    [Fact]
    public void Blazor_web_sitesi_uclari_modul_metadatasi_tasir()
    {
        // RequireWebSitesiModulu filtreyi takarken metadatayı da yazar — mevcut Blazor uçlarında kanıt.
        var ws = fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => Rota(e).StartsWith("/web-sitesi/", StringComparison.OrdinalIgnoreCase)
                        && e.Metadata.GetMetadata<System.Reflection.MethodInfo>() is not null) // minimal API (Razor sayfası değil)
            .ToList();
        Assert.NotEmpty(ws);
        Assert.All(ws, e => Assert.Contains(e.Metadata.GetOrderedMetadata<ModulMetadata>(), m => m.Modul == "WebSitesi"));
    }

    [Theory]
    [InlineData("/api/ui/v1/oturum/ben", true)]
    [InlineData("api/ui/v1/oturum/giris", true)]
    [InlineData("/api/ui/v1/istemci-hata", true)]
    [InlineData("/api/ui/v1/kiralar", false)]
    [InlineData("/api/ui/v1/oturumlar", false)]      // segment sınırı: önek benzerliği muafiyet vermez
    [InlineData("/api/ui/v1/test/tamam", false)]
    [InlineData("/api/ui/v1/platform/kiracilar", true)]   // F12.1: platform oturumunun firması yok
    [InlineData("/api/ui/v1/platformx/kiracilar", false)] // segment sınırı
    public void Pilot_muafiyeti_rota_segmentine_gore(string rota, bool muaf)
        => Assert.Equal(muaf, UiApiExtensions.PilotMuaf(rota));

    [Theory]
    [InlineData("GET", true)]
    [InlineData("HEAD", true)]
    [InlineData("OPTIONS", true)]
    [InlineData("POST", false)]
    [InlineData("PUT", false)]
    [InlineData("PATCH", false)]
    [InlineData("DELETE", false)]
    public void Csrf_yalniz_guvensiz_yontemde(string yontem, bool guvenli)
        => Assert.Equal(guvenli, UiApiExtensions.GuvenliYontem(yontem));
}

/// <summary>
/// F1.2 — OpenAPI anlık görüntüsü: <c>docs/api/ui-v1.json</c> Development'taki <c>/openapi/ui-v1.json</c> ile aynı
/// olmalı (SPA tipleri bu dosyadan üretilecek; kayma derleme hatası olur). Güncellemek için:
/// <c>RACAR_OPENAPI_GUNCELLE=1 dotnet test --filter UiApiOpenApiTests</c>.
/// </summary>
[Collection("web")]
public sealed class UiApiOpenApiTests(WebFixture fx)
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repo kökü bulunamadı.");
    }

    [Fact]
    public async Task Anlik_goruntu_guncel()
    {
        var r = await fx.Web.Istemci().GetAsync("/openapi/ui-v1.json");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var node = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        var paths = node["paths"]!.AsObject().Select(p => p.Key).ToList();
        Assert.Contains("/api/ui/v1/oturum/ben", paths);
        Assert.All(paths, p => Assert.StartsWith("/api/ui/v1/", p)); // Blazor uçları belgeye sızmaz
        Assert.DoesNotContain(paths, p => p.StartsWith("/api/ui/v1/test/", StringComparison.Ordinal)); // test uçları yok

        var guncel = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
        var dosya = Path.Combine(RepoKok(), "docs", "api", "ui-v1.json");
        if (Environment.GetEnvironmentVariable("RACAR_OPENAPI_GUNCELLE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dosya)!);
            await File.WriteAllTextAsync(dosya, guncel);
        }
        Assert.True(File.Exists(dosya), "docs/api/ui-v1.json yok — RACAR_OPENAPI_GUNCELLE=1 ile üretin.");
        Assert.True((await File.ReadAllTextAsync(dosya)).ReplaceLineEndings("\n") == guncel,
            "OpenAPI anlık görüntüsü bayat — RACAR_OPENAPI_GUNCELLE=1 ile güncelleyip commit edin.");
    }
}
