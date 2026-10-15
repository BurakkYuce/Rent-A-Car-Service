using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Branches;
using RentACar.Application.SiteIcerik;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.PublicSite;
using RentACar.PublicSite.Components.Themes;

namespace RentACar.IntegrationTests;

public sealed partial class PublicSiteThemeContractTests
{
    // ---- Kontrast: css'ten değer çözümü + bağımsız WCAG hesabı ----

    [GeneratedRegex(@"@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root\s*\{([^}]*)\}\s*\}")]
    private static partial Regex DarkRoot();

    [GeneratedRegex(@":root\s*\{([^}]*)\}")]
    private static partial Regex RootBlock();

    [GeneratedRegex(@"(--[\w-]+)\s*:\s*([^;]+);")]
    private static partial Regex Declaration();

    private static Dictionary<string, string> Tokens(string css, bool dark)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var darkBlock = DarkRoot().Match(css);
        var light = darkBlock.Success ? css.Remove(darkBlock.Index, darkBlock.Length) : css;
        foreach (Match root in RootBlock().Matches(light))
            foreach (Match d in Declaration().Matches(root.Groups[1].Value)) map[d.Groups[1].Value] = d.Groups[2].Value.Trim();
        if (dark && darkBlock.Success)
            foreach (Match d in Declaration().Matches(darkBlock.Groups[1].Value)) map[d.Groups[1].Value] = d.Groups[2].Value.Trim();
        return map;
    }

    private static string ResolveHex(Dictionary<string, string> map, string token)
    {
        var v = map[token];
        for (var i = 0; i < 10 && v.StartsWith("var(", StringComparison.Ordinal); i++)
            v = map[v[4..v.IndexOf(')')].Trim()];
        Assert.Matches("^#[0-9a-fA-F]{6}$", v);
        return v;
    }

    private static double Lum(string hex)
    {
        double Ch(int i)
        {
            var c = int.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(1) + 0.7152 * Ch(3) + 0.0722 * Ch(5);
    }

    private static double Contrast(string a, string b)
    {
        var (x, y) = (Lum(a), Lum(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Metin_ve_firma_rengi_kontrasti_AA(string key)
    {
        var theme = SiteThemes.Resolve(key);
        var css = ReadCss(ThemeFile(theme));
        foreach (var dark in new[] { false, true })
        {
            var map = Tokens(css, dark);
            var mode = dark ? "koyu" : "açık";
            Assert.True(Contrast(ResolveHex(map, "--ink"), ResolveHex(map, "--bg")) >= 4.5, $"{key} {mode}: ink/bg");
            Assert.True(Contrast(ResolveHex(map, "--ink"), ResolveHex(map, "--surface")) >= 4.5, $"{key} {mode}: ink/surface");
            Assert.True(Contrast(ResolveHex(map, "--ink-muted"), ResolveHex(map, "--bg")) >= 4.5, $"{key} {mode}: ink-muted/bg");
            Assert.True(Contrast(ResolveHex(map, "--ink-subtle"), ResolveHex(map, "--surface")) >= 4.5, $"{key} {mode}: ink-subtle/surface");
        }

        // Firma rengi: sayfaya basılan BrandPalette çıktısı (tema varsayılanıyla) — dolgu üstü yazı AA.
        var palette = BrandPalette.For(theme.DefaultBrand);
        Assert.True(Contrast(palette.Light.OnFill, palette.Light.Fill) >= 4.5, $"{key}: brand-ink/brand açık");
        Assert.True(Contrast(palette.Dark.OnFill, palette.Dark.Fill) >= 4.5, $"{key}: brand-ink/brand koyu");
    }

    // ---- Canlı boru hattı: 3 tema × sayfalar ----

    private async Task<Guid> SeedTenantAsync(string? theme)
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
            NullTenantContext.Instance, NullCurrentUser.Instance);
        var t = new Tenant { Code = "tm" + Guid.NewGuid().ToString("N")[..10], Name = "Tema Deneme", IsActive = true };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        if (theme is not null)
        {
            // Ayar satırı RLS'li: firmanın kendi bağlamında, gerçek servisle yazılır.
            using var host = new TestHost(fx.AppConnectionString);
            using var s = host.ScopeFor(t.Id);
            var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
            var m = await svc.GetAsync();
            m.SiteTemasi = theme;
            await svc.SaveAsync(m);
        }
        return t.Id;
    }

    public static TheoryData<string> ThemeKeys() => [.. SiteThemes.All.Select(t => t.Key)];

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");

    /// <summary>Yayında bir ilan (Fiat Egea 2023, günlük 1500 TL KDV dahil, haftalık toplam 9.100 TL, bir
    /// fotoğraf), altı yayında SSS ("Soru 1?" … "Soru 6?") ve bir aktif şube. Slug döner.</summary>
    private async Task<string> SeedContentAsync(Guid t)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(t);
        var sp = s.ServiceProvider;
        await sp.GetRequiredService<BranchService>().CreateAsync(new BranchInput
        {
            Kod = "TMA", Ad = "Tema Merkez", Il = "İzmir", Ilce = "Konak", Aktif = true,
        });
        var vehicleId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        {
            Plaka = "35TM" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            Marka = "Fiat", Tip = "Egea", Vites = Transmission.Manuel, Yakit = FuelType.Dizel,
            ModelYili = 2023, Durum = VehicleStatus.Musait, GrupBilincliBos = true,
        });
        await sp.GetRequiredService<VehiclePhotoService>().AddAsync(vehicleId, TinyPng);
        var listings = sp.GetRequiredService<WebListingService>();
        var signature = (await listings.PoolAsync()).Single(k => k.Araclar.Count == 1).Imza;
        var listingId = await listings.StepOneSignatureAsync([signature]);
        await listings.StepTwoAsync(listingId, 1500m, 9100m, null, true);
        await listings.StepThreeAsync(listingId, [new OzellikSatiri("Vites", "Manuel")]);
        var content = sp.GetRequiredService<SiteContentService>();
        for (var i = 1; i <= 6; i++)
            await content.SaveFaqAsync(new SssInput(null, $"Soru {i}?", $"Cevap {i}.", i, true));
        return (await listings.GetAsync(listingId))!.Ilan.Slug;
    }

    /// <summary>Sorgu tarihi: bugünden göreli (sabit tarih YOK — TestTarihBombasi), ISO gün.</summary>
    private static string Day(int offset)
        => DateTime.UtcNow.Date.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"<script\b[^>]*>.*?</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptBlock();

    [Theory]
    [MemberData(nameof(ThemeKeys))]
    public async Task Her_tema_sayfalari_basar_satir_ici_betik_ve_dis_kaynak_yok(string key)
    {
        var tenant = await SeedTenantAsync(key);
        var slug = await SeedContentAsync(tenant);
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();
        var theme = SiteThemes.Resolve(key);
        var dated = $"/musaitlik?bas={Day(30)}&bit={Day(33)}";

        foreach (var (path, status) in new[]
                 {
                     ("/", HttpStatusCode.OK), ("/musaitlik", HttpStatusCode.OK), (dated, HttpStatusCode.OK),
                     ($"/araclar/{slug}", HttpStatusCode.OK), ("/iletisim", HttpStatusCode.OK),
                     ("/araclar/olmayan-ilan", HttpStatusCode.NotFound), ("/yok/boyle/bir/adres", HttpStatusCode.NotFound),
                 })
        {
            using var r = await client.GetAsync(path);
            var html = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == status, $"{key} {path} → {(int)r.StatusCode}");
            Assert.True(r.Headers.Contains("Content-Security-Policy"), $"{key} {path}: CSP yok");
            // Kayıtlı temanın css'i yüklendi (parmak izli ad: tema-<ad>.<hash>.css) ve kabuk kanonik anahtarı taşır.
            Assert.Matches($@"css/{Path.GetFileNameWithoutExtension(theme.CssPath)}\.[a-z0-9]+\.css", html);
            Assert.Contains($"data-tema=\"{key}\"", html);
            Assert.Contains("class=\"page", html); // kabuk basıldı
            foreach (Match tag in Regex.Matches(html, "<script[^>]*>"))
            {
                var t = WebUtility.HtmlDecode(tag.Value); // bazı JSON-LD etiketlerinde `+` &#x2B; olarak kodlu
                Assert.True(t.Contains("src=", StringComparison.Ordinal)
                            || t.Contains("application/ld+json", StringComparison.Ordinal), $"{key} {path}: satır içi betik {tag.Value}");
            }
            // Dış KAYNAK yok (CSP 'self'): betik/görsel src'si ve stil/önyükleme/ikon href'i başka kökene gidemez.
            // (Kanonik <link> ve wa.me/harita <a> bağlantıları kaynak değil — kapsam dışı.)
            foreach (Match m in Regex.Matches(html,
                         @"<(?:script|img)[^>]*\ssrc=""(?:https?:)?//|<link[^>]*rel=""(?:stylesheet|preload|icon)""[^>]*href=""(?:https?:)?//",
                         RegexOptions.IgnoreCase))
                Assert.Fail($"{key} {path}: dış kaynak {m.Value}");
            Assert.All(theme.FontPreloads, f => Assert.Contains(Path.GetFileNameWithoutExtension(f), html));

            // Slotlar gerçekten basıldı (temadan bağımsız işaretler). Görünen metin = betik blokları atılmış HTML.
            var visible = WebUtility.HtmlDecode(ScriptBlock().Replace(html, ""));
            Assert.True(Regex.Matches(html, @"<h1[\s>]").Count == 1, $"{key} {path}: tek h1 değil"); // Hero / DetailHero / sayfa
            if (path is "/" or "/musaitlik" || path == dated)
                Assert.Contains($"href=\"/araclar/{slug}\"", html); // Home/Fleet ilanı basıldı
            if (path == dated)
                Assert.Contains("role=\"status\"", html);            // arama sonucu duyurulur (FleetSummary.Canli)
            if (path == "/")
            {
                // SEO L-4: görünen SSS = FAQPage JSON-LD (ana sayfa özeti 4; 5. ve 6. yalnız /sss'te).
                Assert.Equal(4, Regex.Matches(html, @"""@type"":""Question""").Count);
                foreach (var i in new[] { 1, 2, 3, 4 }) Assert.Contains($"Soru {i}?", visible);
                Assert.DoesNotContain("Soru 5?", visible);
            }
            if (path == $"/araclar/{slug}")
            {
                // DetailHero: tarife satırları sayfadan — haftalık toplam 9.100, gün başı 9.100 / 7 = 1.300 (elle).
                Assert.Contains("₺9.100", visible);
                Assert.Contains("₺1.300", visible);
                Assert.Matches($@"name=""ilan""[^>]*value=""{Regex.Escape(slug)}""|value=""{Regex.Escape(slug)}""[^>]*name=""ilan""", html);
            }
        }
    }

    // Boş filo: Fleet slotu sayfanın verdiği boş durumu basar (tarihsiz ve tarihli), ana sayfada filo bölümü yok (T2).
    [Theory]
    [MemberData(nameof(ThemeKeys))]
    public async Task Her_tema_bos_filo_durumunu_basar(string key)
    {
        var tenant = await SeedTenantAsync(key);
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        var browse = WebUtility.HtmlDecode(await client.GetStringAsync("/musaitlik"));
        Assert.Contains("Şu anda sitede yayınlanmış araç yok", browse);
        Assert.DoesNotContain("role=\"status\"", browse);

        var search = WebUtility.HtmlDecode(await client.GetStringAsync($"/musaitlik?bas={Day(30)}&bit={Day(33)}"));
        Assert.Contains("Bu tarihlerde müsait araç yok", search);
        Assert.Contains("role=\"status\"", search);
        Assert.Contains("href=\"/iletisim\"", search);

        var home = await client.GetStringAsync("/");
        Assert.Contains($"data-tema=\"{key}\"", home);
        Assert.DoesNotContain("href=\"/araclar/", home);
    }

    [Fact]
    public async Task Onizleme_parametresi_temayi_degistirir_noindex_yazar_gecersiz_ad_yok_sayilir()
    {
        var tenant = await SeedTenantAsync(null); // kayıtlı tema yok → pazar
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();
        static string Css(string key) => $@"css/{Path.GetFileNameWithoutExtension(SiteThemes.Resolve(key).CssPath)}\.[a-z0-9]+\.css";

        using (var r = await client.GetAsync("/?tema=rota"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Matches(Css("rota"), html);
            Assert.Contains("data-tema=\"rota\"", html);
            Assert.Equal("noindex, follow", string.Join(",", r.Headers.GetValues("X-Robots-Tag")));
        }
        using (var r = await client.GetAsync("/?tema=olmayan"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Matches(Css("pazar"), html);
            Assert.Contains("data-tema=\"pazar\"", html);
            Assert.False(r.Headers.Contains("X-Robots-Tag"));
        }
        using (var r = await client.GetAsync("/"))
        {
            Assert.Contains("data-tema=\"pazar\"", await r.Content.ReadAsStringAsync());
            Assert.False(r.Headers.Contains("X-Robots-Tag"));
        }

        // 404 sayfası da önizlenen temayla basılır (durum 404 kalır); diğer sorgu parametreleri taşınmaz.
        using (var r = await client.GetAsync("/araclar/olmayan-ilan?tema=vitrin&x=1"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Matches(Css("vitrin"), html);
            Assert.Contains("data-tema=\"vitrin\"", html);
            Assert.Contains("burada değil", WebUtility.HtmlDecode(html));
        }
        using (var r = await client.GetAsync("/yok/boyle/bir/adres?tema=olmayan"))
        {
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Contains("data-tema=\"pazar\"", await r.Content.ReadAsStringAsync());
        }
    }

    // Eski önizleme bağlantıları KALICI çalışır: ?tema=tarife → pazar, ?tema=kontuar → rota; yine önizleme sayılır
    // (noindex), 404 sayfası da eşlenen temayla basılır. Kayıtlı tema vitrin: eşleme gerçekten temayı değiştiriyor.
    [Fact]
    public async Task Eski_anahtarli_onizleme_yeni_temayla_basilir_ve_noindex()
    {
        var tenant = await SeedTenantAsync("vitrin");
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        foreach (var (legacy, current) in new[] { ("tarife", "pazar"), ("KONTUAR", "rota") })
        {
            using (var r = await client.GetAsync($"/?tema={legacy}"))
            {
                var html = await r.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                Assert.Contains($"data-tema=\"{current}\"", html);
                Assert.Matches($@"css/{Path.GetFileNameWithoutExtension(SiteThemes.Resolve(current).CssPath)}\.[a-z0-9]+\.css", html);
                Assert.Equal("noindex, follow", string.Join(",", r.Headers.GetValues("X-Robots-Tag")));
            }
            using (var r = await client.GetAsync($"/yok/boyle/bir/adres?tema={legacy}"))
            {
                Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
                Assert.Contains($"data-tema=\"{current}\"", await r.Content.ReadAsStringAsync());
            }
        }
        using (var r = await client.GetAsync("/"))
            Assert.Contains("data-tema=\"vitrin\"", await r.Content.ReadAsStringAsync());
    }

    // Migration'dan önce okunmuş / elle yazılmış eski değer de doğru temayı basar (site ham değeri normalize eder).
    [Fact]
    public async Task Kayitli_eski_anahtar_yeni_temayla_basilir()
    {
        var tenant = await SeedTenantAsync(null);
        await SeedSettingsRowAsync(tenant);           // ayar satırı tembel oluşur
        await WriteRawThemeAsync(tenant, "kontuar");

        using var factory = new PublicSiteFactory(fx, tenant);
        var html = await factory.Client().GetStringAsync("/");
        Assert.Contains("data-tema=\"rota\"", html);
    }

    private async Task SeedSettingsRowAsync(Guid tenant)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(tenant);
        var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
        await svc.SaveAsync(await svc.GetAsync());
    }

    /// <summary>Servisi atlayarak ham değer yazar (servis eski anahtarı normalize ederdi) — firmanın kendi RLS
    /// bağlamında, racar_app ile.</summary>
    private async Task WriteRawThemeAsync(Guid tenant, string value)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand("select set_config('app.tenant_id', @t, true)", conn, tx))
        {
            set.Parameters.AddWithValue("t", tenant.ToString());
            await set.ExecuteScalarAsync();
        }
        await using (var up = new NpgsqlCommand(
                         "update \"Ayarlar\" set \"SiteTemasi\" = @v where \"TenantId\" = @t", conn, tx))
        {
            up.Parameters.AddWithValue("v", value);
            up.Parameters.AddWithValue("t", tenant);
            Assert.Equal(1, await up.ExecuteNonQueryAsync());
        }
        await tx.CommitAsync();
    }
}
