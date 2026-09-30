using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Entities;
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

    [Theory]
    [MemberData(nameof(ThemeKeys))]
    public async Task Her_tema_sayfalari_basar_satir_ici_betik_ve_dis_kaynak_yok(string key)
    {
        var tenant = await SeedTenantAsync(key);
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();
        var theme = SiteThemes.Resolve(key);

        foreach (var (path, status) in new[]
                 {
                     ("/", HttpStatusCode.OK), ("/musaitlik", HttpStatusCode.OK), ("/iletisim", HttpStatusCode.OK),
                     ("/araclar/olmayan-ilan", HttpStatusCode.NotFound), ("/yok/boyle/bir/adres", HttpStatusCode.NotFound),
                 })
        {
            using var r = await client.GetAsync(path);
            var html = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == status, $"{key} {path} → {(int)r.StatusCode}");
            Assert.True(r.Headers.Contains("Content-Security-Policy"), $"{key} {path}: CSP yok");
            // Kayıtlı temanın css'i yüklendi (parmak izli ad: tema-<ad>.<hash>.css).
            Assert.Matches($@"css/tema-{key}\.[a-z0-9]+\.css", html);
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
        }
    }

    [Fact]
    public async Task Onizleme_parametresi_temayi_degistirir_noindex_yazar_gecersiz_ad_yok_sayilir()
    {
        var tenant = await SeedTenantAsync(null); // kayıtlı tema yok → tarife
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        using (var r = await client.GetAsync("/?tema=kontuar"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Matches(@"css/tema-kontuar\.[a-z0-9]+\.css", html);
            Assert.Equal("noindex, follow", string.Join(",", r.Headers.GetValues("X-Robots-Tag")));
        }
        using (var r = await client.GetAsync("/?tema=olmayan"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Matches(@"css/tema-tarife\.[a-z0-9]+\.css", html);
            Assert.False(r.Headers.Contains("X-Robots-Tag"));
        }
        using (var r = await client.GetAsync("/"))
            Assert.False(r.Headers.Contains("X-Robots-Tag"));

        // 404 sayfası da önizlenen temayla basılır (durum 404 kalır); diğer sorgu parametreleri taşınmaz.
        using (var r = await client.GetAsync("/araclar/olmayan-ilan?tema=vitrin&x=1"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Matches(@"css/tema-vitrin\.[a-z0-9]+\.css", html);
            Assert.Contains("burada değil", WebUtility.HtmlDecode(html));
        }
        using (var r = await client.GetAsync("/yok/boyle/bir/adres?tema=olmayan"))
        {
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Matches(@"css/tema-tarife\.[a-z0-9]+\.css", await r.Content.ReadAsStringAsync());
        }
    }
}
