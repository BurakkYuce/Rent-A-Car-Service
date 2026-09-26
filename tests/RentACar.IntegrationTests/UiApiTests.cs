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
    private const string Login = "/api/ui/v1/oturum/giris";
    private const string Logout = "/api/ui/v1/oturum/cikis";
    private const string Xsrf = "/api/ui/v1/oturum/xsrf";

    // ------------------------------------------------------------ yardımcılar

    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private static async Task<JsonElement> Body(HttpResponseMessage r)
        => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task ExpectProblem(HttpResponseMessage r, HttpStatusCode status, string? code)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Beklenen {(int)status}, gelen {(int)r.StatusCode}: {text}");
        Assert.Null(r.Headers.Location); // yönlendirme YOK
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var j = JsonDocument.Parse(text).RootElement;
        Assert.Equal((int)status, j.GetProperty("status").GetInt32());
        Assert.True(j.TryGetProperty("title", out _));
        if (code is null) Assert.False(j.TryGetProperty("kod", out _));
        else Assert.Equal(code, j.GetProperty("kod").GetString());
        Assert.True(r.Headers.CacheControl?.NoStore == true, "no-store eksik");
    }

    private static HttpRequestMessage Request(HttpMethod m, string url, string? xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (xsrf is not null) req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private static async Task<string> GetXsrf(HttpClient c)
    {
        var r = await c.GetAsync(Xsrf);
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        return CookieValue(r, "XSRF-TOKEN") ?? throw new Xunit.Sdk.XunitException("XSRF-TOKEN çerezi verilmedi");
    }

    /// <summary>JSON giriş gövdesi — kimlik fixture'da çalışma anında üretilir (sabit parola yok).</summary>
    private static object LoginBody(TestKimlik k, string? password = null)
        => new { firma = k.Firma, kullanici = k.Kullanici, sifre = password ?? k.Sifre };

    /// <summary>Giriş yapar; (istemci, girişten ÖNCEKİ belirteç, girişten SONRAKİ belirteç) döner.</summary>
    private async Task<(HttpClient C, string Once, string Sonra)> DoLogin(TestKimlik? k = null, WebFactory? f = null)
    {
        var c = (f ?? fx.Web).Client();
        var once = await GetXsrf(c);
        var r = await c.SendAsync(Request(HttpMethod.Post, Login, once, LoginBody(k ?? fx.PilotAdmin)));
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var after = CookieValue(r, "XSRF-TOKEN") ?? throw new Xunit.Sdk.XunitException("girişte XSRF yenilenmedi");
        return (c, once, after);
    }

    // ------------------------------------------------------------ 401 / oturum

    [Fact]
    public async Task Anonim_ben_401_oturum_yok_json_302_degil()
    {
        var r = await fx.Web.Client().GetAsync(Ben);
        await ExpectProblem(r, HttpStatusCode.Unauthorized, "oturum_yok");
    }

    [Fact]
    public async Task Giris_oturum_cerezi_ve_taze_xsrf_verir_ben_doner()
    {
        var c = fx.Web.Client();
        var once = await GetXsrf(c);
        var r = await c.SendAsync(Request(HttpMethod.Post, Login, once, LoginBody(fx.PilotAdmin)));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.NotNull(CookieValue(r, "racar.session"));
        var after = CookieValue(r, "XSRF-TOKEN");
        Assert.NotNull(after);
        Assert.NotEqual(once, after);
        var xsrfCookie = r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        Assert.Contains("path=/", xsrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", xsrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", xsrfCookie, StringComparison.OrdinalIgnoreCase); // JS okuyabilmeli
        Assert.True(r.Headers.CacheControl?.NoStore == true);

        var j = await Body(r);
        Assert.Equal(fx.PilotAdmin.Kullanici, j.GetProperty("kullanici").GetProperty("kullaniciAdi").GetString());
        Assert.Equal("Test Admin", j.GetProperty("kullanici").GetProperty("adSoyad").GetString());
        Assert.Equal(fx.PilotAdmin.Firma, j.GetProperty("kiraci").GetProperty("kod").GetString());
        Assert.Equal("Pilot Test Firması", j.GetProperty("kiraci").GetProperty("ad").GetString());
        Assert.Equal(fx.PilotCompanyId, j.GetProperty("kiraci").GetProperty("id").GetGuid());
        Assert.Equal("Admin", j.GetProperty("rol").GetString());
        var permissions = j.GetProperty("izinler").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("ManageUsers", permissions);
        Assert.Contains("FinanceWrite", permissions);
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
        var (c, _, _) = await DoLogin(fx.PilotOperator);
        var j = await Body(await c.GetAsync(Ben));

        Assert.Equal("Operator", j.GetProperty("rol").GetString());
        var scope = j.GetProperty("subeKapsami");
        Assert.False(scope.GetProperty("tumSubeler").GetBoolean());
        Assert.Equal("Merkez", scope.GetProperty("subeAd").GetString());
        var permissions = j.GetProperty("izinler").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("OperationsWrite", permissions);
        Assert.DoesNotContain("FinanceWrite", permissions);
        Assert.DoesNotContain("ManageUsers", permissions);
    }

    [Fact]
    public async Task Hatali_sifre_400_dogrulama_oturum_acilmaz()
    {
        var c = fx.Web.Client();
        var t = await GetXsrf(c);
        var r = await c.SendAsync(Request(HttpMethod.Post, Login, t, LoginBody(fx.PilotAdmin, WebFixture.RandomPassword())));

        await ExpectProblem(r, HttpStatusCode.BadRequest, "dogrulama");
        Assert.Null(CookieValue(r, "racar.session"));
        await ExpectProblem(await c.GetAsync(Ben), HttpStatusCode.Unauthorized, "oturum_yok");
    }

    [Fact]
    public async Task Cikis_oturumu_kapatir_belirteci_yeniler()
    {
        var (c, _, after) = await DoLogin();

        var r = await c.SendAsync(Request(HttpMethod.Post, Logout, after));
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var afterLogout = CookieValue(r, "XSRF-TOKEN");
        Assert.NotNull(afterLogout);
        await ExpectProblem(await c.GetAsync(Ben), HttpStatusCode.Unauthorized, "oturum_yok");

        // Kullanıcıya bağlı eski belirteç anonim kimlikte reddedilir; çıkışta verilen kabul edilir.
        var old = await c.SendAsync(Request(HttpMethod.Post, Login, after, LoginBody(fx.PilotAdmin)));
        await ExpectProblem(old, HttpStatusCode.BadRequest, "xsrf_gecersiz");
        var newItem = await c.SendAsync(Request(HttpMethod.Post, Login, afterLogout, LoginBody(fx.PilotAdmin)));
        Assert.Equal(HttpStatusCode.OK, newItem.StatusCode);
    }

    // ------------------------------------------------------------ CSRF

    [Fact]
    public async Task Basliksiz_guvensiz_istek_reddedilir()
    {
        var (c, _, after) = await DoLogin();

        await ExpectProblem(await c.SendAsync(Request(HttpMethod.Post, "/api/ui/v1/test/yaz", null)),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
        await ExpectProblem(await c.SendAsync(Request(HttpMethod.Post, "/api/ui/v1/test/yaz", "uydurma-belirtec")),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Request(HttpMethod.Post, "/api/ui/v1/test/yaz", after))).StatusCode);
        // Güvenli yöntem belirteç istemez.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/ui/v1/test/tamam")).StatusCode);
    }

    [Fact]
    public async Task Giristen_once_alinan_belirtec_giristen_sonra_reddedilir()
    {
        var (c, once, after) = await DoLogin();

        await ExpectProblem(await c.SendAsync(Request(HttpMethod.Post, "/api/ui/v1/test/yaz", once)),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Request(HttpMethod.Post, "/api/ui/v1/test/yaz", after))).StatusCode);
    }

    [Fact]
    public async Task Giris_de_csrf_ister()
    {
        var c = fx.Web.Client();
        await GetXsrf(c); // çerez var ama başlık yok
        var r = await c.SendAsync(Request(HttpMethod.Post, Login, null, LoginBody(fx.PilotAdmin)));
        await ExpectProblem(r, HttpStatusCode.BadRequest, "xsrf_gecersiz");
        Assert.Null(CookieValue(r, "racar.session"));
    }

    [Fact]
    public async Task Baska_kullanicinin_belirteci_kabul_edilmez()
    {
        var (_, _, adminToken) = await DoLogin();
        var (op, _, _) = await DoLogin(fx.PilotOperator);

        await ExpectProblem(await op.SendAsync(Request(HttpMethod.Post, "/api/ui/v1/test/yaz", adminToken)),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
    }

    // ------------------------------------------------------------ 403 / 400 / 409 / 500

    [Fact]
    public async Task Servis_YetkiYok_403_yetki_yok()
        => await ExpectProblem(await (await DoLogin()).C.GetAsync("/api/ui/v1/test/yetki-yok"),
            HttpStatusCode.Forbidden, "yetki_yok");

    [Fact]
    public async Task Uc_izin_kapisi_403_yetki_yok_yetkisiz_sayfasina_yonlendirmez()
    {
        var (c, _, _) = await DoLogin(fx.PilotOperator);
        await ExpectProblem(await c.GetAsync("/api/ui/v1/test/finans"), HttpStatusCode.Forbidden, "yetki_yok");

        var (admin, _, _) = await DoLogin();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/ui/v1/test/finans")).StatusCode);
    }

    [Fact]
    public async Task Pilot_olmayan_firma_403_pilot_degil_oturum_uclari_acik()
    {
        var (c, _, after) = await DoLogin(fx.OtherAdmin);

        await ExpectProblem(await c.GetAsync("/api/ui/v1/test/tamam"), HttpStatusCode.Forbidden, "pilot_degil");
        await ExpectProblem(await c.SendAsync(Request(HttpMethod.Post, "/api/ui/v1/test/yaz", after)),
            HttpStatusCode.Forbidden, "pilot_degil");

        var ben = await c.GetAsync(Ben); // oturum/* pilot kapısından muaf
        Assert.Equal(HttpStatusCode.OK, ben.StatusCode);
        Assert.False((await Body(ben)).GetProperty("pilot").GetBoolean());

        var (pilot, _, _) = await DoLogin();
        Assert.Equal(HttpStatusCode.OK, (await pilot.GetAsync("/api/ui/v1/test/tamam")).StatusCode);
    }

    [Fact]
    public async Task Pilot_bayragi_kapatilinca_aninda_kapanir()
    {
        var k = await fx.CompanyAndUserAsync("Pilot Aç-Kapa Firması");
        var newItem = await fx.TenantIdAsync(k.Firma);
        await fx.MakePilotAsync(newItem, true);
        var (c, _, _) = await DoLogin(k);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/ui/v1/test/tamam")).StatusCode);

        await fx.MakePilotAsync(newItem, false);
        await ExpectProblem(await c.GetAsync("/api/ui/v1/test/tamam"), HttpStatusCode.Forbidden, "pilot_degil");
    }

    [Fact]
    public async Task Alanli_ValidationException_400_errors_tasir()
    {
        var (c, _, t) = await DoLogin();
        var r = await c.SendAsync(Request(HttpMethod.Post, "/api/ui/v1/test/dogrulama", t));

        await ExpectProblem(r, HttpStatusCode.BadRequest, "dogrulama");
        var j = await Body(r);
        Assert.Equal("Plaka zorunludur.", j.GetProperty("detail").GetString());
        Assert.Equal("Plaka zorunludur.", j.GetProperty("errors").GetProperty("Plaka")[0].GetString());
    }

    [Fact]
    public async Task Cakisma_ve_mukerrer_ayri_409_kodlari()
    {
        var (c, _, t) = await DoLogin();

        await ExpectProblem(await c.SendAsync(Request(HttpMethod.Post, "/api/ui/v1/test/cakisma", t)),
            HttpStatusCode.Conflict, "cakisma");
        await ExpectProblem(await c.SendAsync(Request(HttpMethod.Post, "/api/ui/v1/test/mukerrer", t)),
            HttpStatusCode.Conflict, "mukerrer");
    }

    [Fact]
    public async Task Beklenmeyen_hata_500_problem_ayrinti_sizdirmaz()
    {
        var r = await (await DoLogin()).C.GetAsync("/api/ui/v1/test/patla");

        await ExpectProblem(r, HttpStatusCode.InternalServerError, null);
        Assert.DoesNotContain(TestUiEndpoints.HiddenDetail, await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Bozuk_json_400_500_degil()
    {
        var c = fx.Web.Client();
        var t = await GetXsrf(c);
        var req = Request(HttpMethod.Post, Login, t);
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
    public async Task Bozuk_json_her_ortamda_400_kod_dogrulama(bool developmentLike)
    {
        await using var f = fx.Web.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = developmentLike)));
        var c = f.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var t = await GetXsrf(c);
        var req = Request(HttpMethod.Post, Login, t);
        req.Content = new StringContent("{bozuk", Encoding.UTF8, "application/json");
        var r = await c.SendAsync(req);

        await ExpectProblem(r, HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal("İstek gövdesi okunamadı ya da eksik.", (await Body(r)).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("22001", LogLevel.Warning)]
    [InlineData("22003", LogLevel.Warning)]
    [InlineData("23505", LogLevel.Error)]   // tanınmayan DB hatası: 500 + Error (gizlenmez)
    public void Veri_tasmasi_agi_Warning_loglanir(string sqlState, LogLevel expected)
    {
        var ex = new Microsoft.EntityFrameworkCore.DbUpdateException("x",
            new PostgresException("taşma", "ERROR", "ERROR", sqlState));
        Assert.Equal(expected, UiApiExtensions.LevelFor(ex));
    }

    [Fact]
    public void Istemci_hatasi_Information_beklenmeyen_Error()
    {
        Assert.Equal(LogLevel.Information, UiApiExtensions.LevelFor(new RentACar.Application.Common.ValidationException("x")));
        Assert.Equal(LogLevel.Information, UiApiExtensions.LevelFor(new BadHttpRequestException("x")));
        Assert.Equal(LogLevel.Error, UiApiExtensions.LevelFor(new InvalidOperationException("x")));
    }

    [Fact]
    public async Task Bilinmeyen_rota_ve_yanlis_yontem_json_html_degil()
    {
        var c = fx.Web.Client();
        await ExpectProblem(await c.GetAsync("/api/ui/v1/boyle-bir-uc-yok"), HttpStatusCode.NotFound, null);
        await ExpectProblem(await c.GetAsync("/api/ui/v1/test"), HttpStatusCode.NotFound, null);
        var wrongMethod = await c.GetAsync(Login); // POST-only uca GET: 404/405, ama her durumda JSON
        Assert.True(wrongMethod.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        Assert.Equal("application/problem+json", wrongMethod.Content.Headers.ContentType?.MediaType);
    }

    // ------------------------------------------------------------ kiracı kapalı / platform / hız sınırı

    [Fact]
    public async Task Kapali_firma_401_kiraci_kapali_ve_oturum_duser()
    {
        var k = await fx.CompanyAndUserAsync("Kapanacak Firma");
        var id = await fx.TenantIdAsync(k.Firma);
        var (c, _, _) = await DoLogin(k);
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

        await ExpectProblem(await c.GetAsync(Ben), HttpStatusCode.Unauthorized, "kiraci_kapali");
        await ExpectProblem(await c.GetAsync(Ben), HttpStatusCode.Unauthorized, "oturum_yok"); // çerez silindi
    }

    [Fact]
    public async Task Platform_operatoru_ui_verisine_403_ben_401_yonlendirme_yok()
    {
        // F13.1a: Blazor platform giriş formu kalktı; platform oturumu yeni arayüzün platform ucuyla (aynı çerez).
        var c = fx.Web.Client();
        var xsrf = CookieValue(await c.GetAsync(Xsrf), "XSRF-TOKEN");
        var entry = await c.SendAsync(Request(HttpMethod.Post, "/api/ui/v1/platform/oturum/giris", xsrf,
            new { kullanici = fx.Platform.Kullanici, sifre = fx.Platform.Sifre }));
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        Assert.NotNull(CookieValue(entry, "racar.session"));

        await ExpectProblem(await c.GetAsync("/api/ui/v1/test/tamam"), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await c.GetAsync(Ben), HttpStatusCode.Unauthorized, "oturum_yok"); // muaf ama firma oturumu yok
        Assert.Equal(HttpStatusCode.NoContent, (await c.GetAsync(Xsrf)).StatusCode);
    }

    [Fact]
    public async Task Giris_hiz_siniri_429_cok_istek_json()
    {
        var c = fx.NarrowLimited.Client(); // limit 2
        var body = LoginBody(fx.PilotAdmin, WebFixture.RandomPassword());
        await c.SendAsync(Request(HttpMethod.Post, Login, null, body));
        await c.SendAsync(Request(HttpMethod.Post, Login, null, body));
        await ExpectProblem(await c.SendAsync(Request(HttpMethod.Post, Login, null, body)),
            HttpStatusCode.TooManyRequests, "cok_istek");
    }

    // ------------------------------------------------------------ Blazor değişmedi

    [Fact]
    public async Task Blazor_yonlendirmeleri_degismedi()
    {
        var c = fx.Web.Client();
        // F13.1a: Blazor Panel ("/") silindi — oturumsuz "/" artık challenge almaz (F13.1b pilotsuz yönlendirme ekler).

        var pickup = await c.PostAsync("/auth/logout", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Redirect, pickup.StatusCode);
        Assert.Equal("/login", pickup.Headers.Location?.OriginalString);

        // Blazor girişi hâlâ form + yönlendirme; aynı claim setiyle /api/ui/ben'i de açar (tek cookie şeması).
        var entry = await c.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["firma"] = fx.PilotAdmin.Firma, ["kullanici"] = fx.PilotAdmin.Kullanici, ["sifre"] = fx.PilotAdmin.Sifre,
        }));
        Assert.Equal(HttpStatusCode.Redirect, entry.StatusCode);
        Assert.Equal("/", entry.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Ben)).StatusCode);

        // Bilinmeyen Blazor yolu hâlâ StatusCodePages'in not-found sayfası (ProblemDetails değil).
        var none = await c.GetAsync("/boyle-bir-sayfa-yok");
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal("text/html", none.Content.Headers.ContentType?.MediaType);
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
    private static readonly Dictionary<string, string> ModulePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/api/ui/v1/web-sitesi"] = "WebSitesi",
    };

    private List<RouteEndpoint> UiEndpoints()
        => fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => ("/" + (e.RoutePattern.RawText ?? "").TrimStart('/')).StartsWith("/api/ui", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string RouteOf(RouteEndpoint e) => "/" + (e.RoutePattern.RawText ?? "").TrimStart('/');

    [Fact]
    public void Oturum_uclari_kayitli()
    {
        var routes = UiEndpoints().Select(RouteOf).ToList();
        foreach (var r in new[] { "/api/ui/v1/oturum/giris", "/api/ui/v1/oturum/cikis", "/api/ui/v1/oturum/ben", "/api/ui/v1/oturum/xsrf" })
            Assert.Contains(r, routes);
    }

    [Fact]
    public void Her_ui_ucu_grupta_ve_izinli_ya_da_acikca_muaf()
    {
        var endpoints = UiEndpoints();
        Assert.NotEmpty(endpoints);
        var violation = endpoints
            .Where(e => e.Metadata.GetMetadata<UiApiGrubuMetadata>() is null
                        || (e.Metadata.GetMetadata<IzinMetadata>() is null
                            && e.Metadata.GetMetadata<IzinlerdenBiriMetadata>() is null // F4.1: "izinlerden biri" kapısı
                            && e.Metadata.GetMetadata<IzinMuafMetadata>() is null))
            .Select(RouteOf).ToList();
        Assert.True(violation.Count == 0, "Grupsuz ya da izinsiz /api/ui ucu: " + string.Join(", ", violation));
    }

    [Fact]
    public void Her_ui_ucu_TypedResults_ile_yanit_turu_bildirir()
    {
        // TypedResults zorunlu. Ayrıca yakalar: yalnız (HttpContext) alıp Task<T> dönen yöntem grubu
        // RequestDelegate aşırı yüklemesine bağlanır, dönüş değeri yok sayılır (200 boş gövde) — o uçta
        // yanıt türü metadatası OLMAZ.
        var violation = UiEndpoints()
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>() is null)
            .Select(RouteOf).ToList();
        Assert.True(violation.Count == 0, "Yanıt türü bildirmeyen /api/ui ucu: " + string.Join(", ", violation));
    }

    [Fact]
    public void Muafiyet_yalniz_oturum_uclarinda()
    {
        // F1.6: /menu da muaf — her oturumun bir menüsü var, kapı ÖĞE düzeyinde (MenuApi.Gorunur).
        // F3.5: /tablo-duzenleri/* muaf — kişisel arayüz tercihi; kullanıcı oturumdan gelir (uçta kullanıcı
        // parametresi yok), iş verisi taşımaz. Kullanıcı/firma izolasyonu TabloDuzeniTests'te kilitli.
        var exempt = UiEndpoints().Where(e => e.Metadata.GetMetadata<IzinMuafMetadata>() is not null
                                         && e.Metadata.GetMetadata<IzinMetadata>() is null
                                         && e.Metadata.GetMetadata<IzinlerdenBiriMetadata>() is null)
            .Select(RouteOf).ToList();
        // F3.3: /istemci-hata da muaf — her oturum yalnız KENDİ tarayıcı hatasını raporlar (veri yok, yalnız log).
        Assert.All(exempt, r => Assert.True(r.StartsWith("/api/ui/v1/oturum/", StringComparison.Ordinal)
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
                                          // F11.1a: Blazor sayfaları yalnız [Authorize] — belge listesini/indirmeyi sahadaki
                                          // her personel (muhasebe dahil) görür; firma belgelerinde yönetici bayrağı serviste.
                                          // Yükleme/silme OperationsWrite taşır (bu listeye düşmez). UiTanimTests kilitler.
                                          || r is "/api/ui/v1/dokumanlar" or "/api/ui/v1/dokumanlar/"
                                          || r == "/api/ui/v1/firma-belgeleri"
                                          // F11.1a: kullanıcının KENDİ takvim bağlantısı (oturum kullanıcısından; kimlik
                                          // parametresi yok) — Blazor sayfası ve /takvim/yenile ucu yalnız oturum ister.
                                          || r.StartsWith("/api/ui/v1/takvim-abonelik", StringComparison.Ordinal)
                                          || r.StartsWith("/api/ui/v1/tablo-duzenleri/", StringComparison.Ordinal), r));
    }

    [Fact]
    public void Platform_endpoints_carry_the_platform_policy_and_only_login_logout_are_anonymous()
    {
        // F12.1: every /api/ui/v1/platform endpoint is behind "PlatformAdmin"; the only anonymous ones are
        // login/logout. A new platform endpoint without the policy would be reachable by any tenant user
        // (IzinMuaf skips the tenant permission matrix) — this is the fence.
        var platform = UiEndpoints().Where(e => RouteOf(e).StartsWith("/api/ui/v1/platform/", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(platform);
        var anonymous = platform.Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null)
            .Select(RouteOf).OrderBy(r => r, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "/api/ui/v1/platform/oturum/cikis", "/api/ui/v1/platform/oturum/giris" }, anonymous);
        var missing = platform
            .Where(e => !e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                .Any(a => a.Policy == "PlatformAdmin"))
            .Select(RouteOf).ToList();
        Assert.True(missing.Count == 0, "PlatformAdmin policy eksik: " + string.Join(", ", missing));
        // And the policy is not used outside the platform area of the UI API.
        var leaked = UiEndpoints().Where(e => !RouteOf(e).StartsWith("/api/ui/v1/platform/", StringComparison.Ordinal)
                                           && e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                                               .Any(a => a.Policy == "PlatformAdmin"))
            .Select(RouteOf).ToList();
        Assert.Empty(leaked);
    }

    [Fact]
    public void Modul_yolundaki_uc_modul_metadatasi_tasir()
    {
        var violation = new List<string>();
        foreach (var e in UiEndpoints())
        foreach (var (prefix, module) in ModulePaths)
            if (RouteOf(e).StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
                && e.Metadata.GetOrderedMetadata<ModulMetadata>().All(m => m.Modul != module))
                violation.Add(RouteOf(e));
        Assert.True(violation.Count == 0, "Modül metadatası eksik: " + string.Join(", ", violation));
    }

    // F13.1a: "Blazor web sitesi uçları modül metadatası taşır" testi silindi — /web-sitesi/* Blazor form uçları kalktı.
    // Aynı kural /api/ui web sitesi uçlarında Modul_yolundaki_uc_modul_metadatasi_tasir ile kilitli.

    [Theory]
    [InlineData("/api/ui/v1/oturum/ben", true)]
    [InlineData("api/ui/v1/oturum/giris", true)]
    [InlineData("/api/ui/v1/istemci-hata", true)]
    [InlineData("/api/ui/v1/kiralar", false)]
    [InlineData("/api/ui/v1/oturumlar", false)]      // segment sınırı: önek benzerliği muafiyet vermez
    [InlineData("/api/ui/v1/test/tamam", false)]
    [InlineData("/api/ui/v1/platform/kiracilar", true)]   // F12.1: platform oturumunun firması yok
    [InlineData("/api/ui/v1/platformx/kiracilar", false)] // segment sınırı
    public void Pilot_muafiyeti_rota_segmentine_gore(string route, bool exempt)
        => Assert.Equal(exempt, UiApiExtensions.PilotExempt(route));

    [Theory]
    [InlineData("GET", true)]
    [InlineData("HEAD", true)]
    [InlineData("OPTIONS", true)]
    [InlineData("POST", false)]
    [InlineData("PUT", false)]
    [InlineData("PATCH", false)]
    [InlineData("DELETE", false)]
    public void Csrf_yalniz_guvensiz_yontemde(string method, bool safe)
        => Assert.Equal(safe, UiApiExtensions.SafeMethod(method));
}

/// <summary>
/// F1.2 — OpenAPI anlık görüntüsü: <c>docs/api/ui-v1.json</c> Development'taki <c>/openapi/ui-v1.json</c> ile aynı
/// olmalı (SPA tipleri bu dosyadan üretilecek; kayma derleme hatası olur). Güncellemek için:
/// <c>RACAR_OPENAPI_GUNCELLE=1 dotnet test --filter UiApiOpenApiTests</c>.
/// </summary>
[Collection("web")]
public sealed class UiApiOpenApiTests(WebFixture fx)
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repo kökü bulunamadı.");
    }

    [Fact]
    public async Task Anlik_goruntu_guncel()
    {
        var r = await fx.Web.Client().GetAsync("/openapi/ui-v1.json");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var node = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        var paths = node["paths"]!.AsObject().Select(p => p.Key).ToList();
        Assert.Contains("/api/ui/v1/oturum/ben", paths);
        Assert.All(paths, p => Assert.StartsWith("/api/ui/v1/", p)); // Blazor uçları belgeye sızmaz
        Assert.DoesNotContain(paths, p => p.StartsWith("/api/ui/v1/test/", StringComparison.Ordinal)); // test uçları yok

        var current = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
        var file = Path.Combine(RepoRoot(), "docs", "api", "ui-v1.json");
        if (Environment.GetEnvironmentVariable("RACAR_OPENAPI_GUNCELLE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, current);
        }
        Assert.True(File.Exists(file), "docs/api/ui-v1.json yok — RACAR_OPENAPI_GUNCELLE=1 ile üretin.");
        Assert.True((await File.ReadAllTextAsync(file)).ReplaceLineEndings("\n") == current,
            "OpenAPI anlık görüntüsü bayat — RACAR_OPENAPI_GUNCELLE=1 ile güncelleyip commit edin.");
    }
}
