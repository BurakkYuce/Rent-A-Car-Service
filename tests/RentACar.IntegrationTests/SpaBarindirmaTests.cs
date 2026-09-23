using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Spa;

namespace RentACar.IntegrationTests;

/// <summary>
/// F1.5 — <c>/app</c> barındırma: saf karar fonksiyonları (dizin çözümü, dosya/rota ayrımı, önbellek başlığı).
/// BAĞIMSIZ ORACLE: beklenen değerler elle yazılmış sabitlerdir (sınıfın kendi sabitleri kullanılmaz).
/// </summary>
public sealed class SpaBarindirmaKararTests
{
    [Theory]
    [InlineData("/opt/racar/releases/r1/web", null, "/opt/racar/releases/r1/app/browser")]
    [InlineData("/opt/racar/releases/r1/web", "", "/opt/racar/releases/r1/app/browser")]
    [InlineData("/opt/racar/releases/r1/web", "../app/browser", "/opt/racar/releases/r1/app/browser")]
    [InlineData("/opt/racar/releases/r1/web", "spa", "/opt/racar/releases/r1/web/spa")]
    [InlineData("/opt/racar/releases/r1/web", "/srv/spa", "/srv/spa")]
    public void Dizin_content_root_a_gore_cozulur(string contentRoot, string? ayar, string beklenen)
        => Assert.Equal(beklenen, SpaBarindirma.KokDizin(contentRoot, ayar));

    [Theory]
    [InlineData("/kiralar/5", false)]
    [InlineData("/kiralar/", false)]
    [InlineData("/giris", false)]
    [InlineData("/a.b/c", false)]
    [InlineData("/main-abc.js", true)]
    [InlineData("/assets/logo.png", true)]
    [InlineData("/favicon.ico", true)]
    public void Uzantili_istek_dosya_uzantisiz_istek_rotadir(string altYol, bool dosyaMi)
        => Assert.Equal(dosyaMi, SpaBarindirma.DosyaIstegiMi(altYol));

    [Theory]
    [InlineData("index.html", "no-cache")]
    [InlineData("favicon.ico", "no-cache")]
    [InlineData("main.js", "no-cache")]
    [InlineData("manifest-webmanifest.json", "no-cache")]
    [InlineData("3rdpartylicenses.txt", "no-cache")]
    [InlineData("main-2QJ7N5ZT.js", "public, max-age=31536000, immutable")]
    [InlineData("chunk-4GXQWSUJ.js", "public, max-age=31536000, immutable")]
    [InlineData("polyfills-FFHMD2TL.js", "public, max-age=31536000, immutable")]
    [InlineData("styles-5INURTSO.css", "public, max-age=31536000, immutable")]
    [InlineData("media/roboto-ABCD1234.woff2", "public, max-age=31536000, immutable")]
    public void Onbellek_basligi_hashe_gore(string dosya, string beklenen)
        => Assert.Equal(beklenen, SpaBarindirma.OnbellekBasligi(dosya));
}

/// <summary>
/// F1.5 — gerçek Web boru hattı (Program.cs) üzerinden <c>/app</c>: anonim kabuk, CSP, fallback, 404'ler,
/// önbellek başlıkları ve kurulmamış SPA. Mevcut Blazor girişinin bozulmadığı da doğrulanır.
/// </summary>
[Collection("spa-web")]
public sealed class SpaBarindirmaHostTests(SpaWebFixture fx)
{
    private const string KabukIsareti = "SPA-KABUK-TEST-7F3A";

    private static HttpClient Istemci(WebApplicationFactory<RentACar.Web.Common.DogrulamaHatasiMiddleware> f)
        => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Kabuk_anonim_200_html_ve_CSP_li()
    {
        var yanit = await Istemci(fx.Kurulu).GetAsync("/app/");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.Null(yanit.Headers.Location); // /login'e yönlendirme YOK
        Assert.Equal("text/html", yanit.Content.Headers.ContentType?.MediaType);
        Assert.Contains(KabukIsareti, await yanit.Content.ReadAsStringAsync());
        var csp = Assert.Single(yanit.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self';", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Equal("nosniff", Assert.Single(yanit.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("SAMEORIGIN", Assert.Single(yanit.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-cache", yanit.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Egik_cizgisiz_app_app_slash_a_yonlenir()
    {
        var yanit = await Istemci(fx.Kurulu).GetAsync("/app?x=1");

        Assert.Equal(HttpStatusCode.MovedPermanently, yanit.StatusCode);
        Assert.Equal("/app/?x=1", yanit.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/app/kiralar/5")]
    [InlineData("/app/giris")]          // Exit: /app/giris → /login döngüsü YOK
    [InlineData("/app/kiralar/")]
    public async Task Istemci_rotasi_index_html_e_duser(string yol)
    {
        var yanit = await Istemci(fx.Kurulu).GetAsync(yol);

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.Null(yanit.Headers.Location);
        Assert.Equal("text/html", yanit.Content.Headers.ContentType?.MediaType);
        Assert.Contains(KabukIsareti, await yanit.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", yanit.Headers.CacheControl?.ToString());
        Assert.True(yanit.Headers.Contains("Content-Security-Policy"));
    }

    [Theory]
    [InlineData("/app/yok.js")]
    [InlineData("/app/main-ZZZZ9999.js")]
    [InlineData("/app/assets/yok.png")]
    public async Task Olmayan_uzantili_dosya_404_index_degil(string yol)
    {
        var yanit = await Istemci(fx.Kurulu).GetAsync(yol);

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
        Assert.Null(yanit.Headers.Location);
        Assert.DoesNotContain(KabukIsareti, await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Hashli_dosya_kalici_onbellekli()
    {
        var yanit = await Istemci(fx.Kurulu).GetAsync("/app/main-ABCD1234.js");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.Equal("text/javascript", yanit.Content.Headers.ContentType?.MediaType);
        Assert.Equal("console.log('spa');", await yanit.Content.ReadAsStringAsync());
        Assert.Equal("public, max-age=31536000, immutable", yanit.Headers.CacheControl?.ToString());
        Assert.True(yanit.Headers.Contains("Content-Security-Policy"));

        var parca = await Istemci(fx.Kurulu).GetAsync("/app/chunk-XYZ98765.js");
        Assert.Equal(HttpStatusCode.OK, parca.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", parca.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Hashsiz_dosya_yeniden_dogrulanir()
    {
        var yanit = await Istemci(fx.Kurulu).GetAsync("/app/favicon.ico");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.Equal("no-cache", yanit.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Kok_disi_dosya_sizmaz()
    {
        var c = Istemci(fx.Kurulu);
        foreach (var yol in new[] { "/app/%2E%2E/gizli.txt", "/app/..%2Fgizli.txt", "/app/%2e%2e%2fgizli.txt" })
        {
            var yanit = await c.GetAsync(yol);
            Assert.NotEqual(HttpStatusCode.InternalServerError, yanit.StatusCode);
            Assert.DoesNotContain("GIZLI-ICERIK", await yanit.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("/app/")]
    [InlineData("/app/kiralar/5")]
    [InlineData("/app/main-ABCD1234.js")]
    public async Task SPA_kurulmamissa_404_500_degil(string yol)
    {
        var yanit = await Istemci(fx.Kurulmamis).GetAsync(yol);

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
        Assert.Null(yanit.Headers.Location);
    }

    [Fact]
    public async Task Tek_giris_Blazor_login_SPA_girisine_yonlenir_ve_acilir()
    {
        var c = Istemci(fx.Kurulu);

        // F4.6: GET /login artık form çizmez → /app/giris (anonim; SPA kurulu host'ta kabuk 200).
        var giris = await c.GetAsync("/login");
        Assert.Equal(HttpStatusCode.Redirect, giris.StatusCode);
        Assert.Equal("/app/giris", giris.Headers.Location?.OriginalString);
        var spaGiris = await c.GetAsync("/app/giris");
        Assert.Equal(HttpStatusCode.OK, spaGiris.StatusCode);
        Assert.Null(spaGiris.Headers.Location);
        Assert.Contains(KabukIsareti, await spaGiris.Content.ReadAsStringAsync());

        // Korumalı Blazor sayfası anonim istekte hâlâ /login'e gider (challenge yalnız /app'ten kalktı).
        var ana = await c.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, ana.StatusCode);
        Assert.StartsWith("/login", ana.Headers.Location?.OriginalString);
    }
}

/// <summary>
/// İki Web host'u (SPA kurulu / kurulmamış) + ayrı test DB'si. Web açılışı migration + seed yaptığı için
/// paylaşımlı "postgres" koleksiyon DB'si KULLANILMAZ (yucerent/demo tohumları başka testlerle çakışırdı).
/// Paralellik kapalı: Web açılışı statik <c>FormSecurity.EnforceAntiforgery</c>'yi yazar
/// (FormSecurityTests ile yarışmasın).
/// </summary>
public sealed class SpaWebFixture : IAsyncLifetime
{
    private readonly PostgresFixture _pg = new();
    private readonly string _gecici = Path.Combine(Path.GetTempPath(), "racar-spa-" + Guid.NewGuid().ToString("N"));

    public SpaWebFactory Kurulu { get; private set; } = default!;
    public SpaWebFactory Kurulmamis { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _pg.InitializeAsync();

        var spa = Path.Combine(_gecici, "browser");
        Directory.CreateDirectory(spa);
        await File.WriteAllTextAsync(Path.Combine(spa, "index.html"),
            "<!doctype html><html><head><base href=\"/app/\"></head><body><app-root>SPA-KABUK-TEST-7F3A</app-root>" +
            "<script src=\"main-ABCD1234.js\" type=\"module\"></script></body></html>");
        await File.WriteAllTextAsync(Path.Combine(spa, "main-ABCD1234.js"), "console.log('spa');");
        await File.WriteAllTextAsync(Path.Combine(spa, "chunk-XYZ98765.js"), "export const a = 1;");
        await File.WriteAllBytesAsync(Path.Combine(spa, "favicon.ico"), [0, 0, 1, 0]);
        await File.WriteAllTextAsync(Path.Combine(_gecici, "gizli.txt"), "GIZLI-ICERIK");

        Kurulu = new SpaWebFactory(_pg, spa, Path.Combine(_gecici, "log-kurulu-.log"));
        Kurulmamis = new SpaWebFactory(_pg, Path.Combine(_gecici, "yok", "browser"),
            Path.Combine(_gecici, "log-kurulmamis-.log"));
    }

    public async Task DisposeAsync()
    {
        await Kurulu.DisposeAsync();
        await Kurulmamis.DisposeAsync();
        await _pg.DisposeAsync();
        try { Directory.Delete(_gecici, recursive: true); } catch { /* best-effort */ }
    }
}

[CollectionDefinition("spa-web", DisableParallelization = true)]
public sealed class SpaWebCollection : ICollectionFixture<SpaWebFixture>;

/// <summary>RentACar.Web'i bellek-içi host eder; arka plan işleri (TCMB çekimi vb.) kapatılır.</summary>
public sealed class SpaWebFactory(PostgresFixture pg, string spaDizin, string logYolu)
    : WebApplicationFactory<RentACar.Web.Common.DogrulamaHatasiMiddleware>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", pg.AppConnectionString);
        builder.UseSetting("ConnectionStrings:Migrator", pg.OwnerConnectionString);
        builder.UseSetting("Spa:Dizin", spaDizin);
        builder.UseSetting("Logging:FilePath", logYolu);
        builder.ConfigureTestServices(s =>
        {
            var isler = s.Where(d => d.ServiceType == typeof(IHostedService)
                                     && d.ImplementationType?.Namespace?.StartsWith("RentACar", StringComparison.Ordinal) == true)
                         .ToList();
            foreach (var d in isler) s.Remove(d);
        });
    }
}
