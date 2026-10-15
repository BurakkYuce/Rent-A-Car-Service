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

    /// <summary>
    /// Tema css'indeki palet token'ları YEDEKTİR (palet bloğu basılamazsa): değerleri temanın varsayılan setiyle aynı
    /// olmalı — yoksa blok yokken site başka renkte görünür. Vurgu yalnız set kendi vurgusunu taşıyorsa karşılaştırılır
    /// (vitrin: vurgu ana rengi izler, css de <c>var(--brand)</c>).
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void Tema_css_yedekleri_varsayilan_setle_ayni(string key)
    {
        var theme = SiteThemes.Resolve(key);
        var css = ReadCss(ThemeFile(theme));
        var palette = theme.DefaultPalette;
        foreach (var (dark, scheme) in new[] { (false, palette.Light), (true, palette.Dark) })
        {
            var map = Tokens(css, dark);
            var mode = dark ? "koyu" : "açık";
            var expected = new Dictionary<string, string?>
            {
                ["--bg"] = scheme.Bg, ["--bg-alt"] = scheme.BgAlt, ["--surface"] = scheme.Surface,
                ["--surface-raised"] = scheme.SurfaceRaised, ["--ink"] = scheme.Ink, ["--ink-muted"] = scheme.InkMuted,
                ["--ink-subtle"] = scheme.InkSubtle, ["--line"] = scheme.Line, ["--line-strong"] = scheme.LineStrong,
                ["--accent"] = scheme.Accent,
            };
            foreach (var (token, value) in expected)
            {
                if (value is null) continue;
                Assert.True(value == ResolveHex(map, token), $"{key} {mode}: {token} yedeği {ResolveHex(map, token)}, set {value}");
            }
        }
        var light = Tokens(css, dark: false);
        Assert.Equal(
            (palette.Light.Bg, palette.Light.Ink, palette.Dark.Bg, palette.Dark.Ink),
            (ResolveHex(light, "--day-bg"), ResolveHex(light, "--day-ink"), ResolveHex(light, "--night-bg"),
                ResolveHex(light, "--night-ink")));
    }

    /// <summary>Durum renkleri (hata/başarı) palete bağlı değil (gerekçeli muafiyet): her setin her şemasında zemin ve
    /// kart üstünde metin olarak AA kalmalı.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void Durum_renkleri_her_setin_zemininde_AA(string key)
    {
        var theme = SiteThemes.Resolve(key);
        var css = ReadCss(ThemeFile(theme));
        foreach (var palette in theme.Palettes)
        {
            var t = SitePaletteCss.Tokens(palette, null);
            foreach (var (dark, scheme) in new[] { (false, t.Light), (true, t.Dark) })
            {
                var map = Tokens(css, dark);
                foreach (var status in new[] { "--danger", "--ok" })
                foreach (var ground in new[] { scheme.Bg, scheme.Surface })
                {
                    var c = Contrast(ResolveHex(map, status), ground.Hex);
                    Assert.True(c >= 4.5, $"{key}/{palette.Key} {(dark ? "koyu" : "açık")}: {status}/{ground.Hex} = {c:0.00}");
                }
            }
        }
    }

    // ---- Canlı boru hattı: 3 tema × sayfalar ----

    private async Task<Guid> SeedTenantAsync(string? theme, string? palette = null, string? color = null)
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
            NullTenantContext.Instance, NullCurrentUser.Instance);
        var t = new Tenant { Code = "tm" + Guid.NewGuid().ToString("N")[..10], Name = "Tema Deneme", IsActive = true };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        if (theme is not null || palette is not null || color is not null)
        {
            // Ayar satırı RLS'li: firmanın kendi bağlamında, gerçek servisle yazılır.
            using var host = new TestHost(fx.AppConnectionString);
            using var s = host.ScopeFor(t.Id);
            var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
            var m = await svc.GetAsync();
            m.SiteTemasi = theme;
            m.SitePaleti = palette;
            m.SiteVurguRengi = color;
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

    private static string? RobotsHeader(HttpResponseMessage r)
        => r.Headers.TryGetValues("X-Robots-Tag", out var v) ? string.Join(",", v) : null;

    /// <summary>
    /// <c>?palet=</c> ve <c>?renk=</c> önizlemesi <c>?tema=</c> ile aynı desen: geçerli değer o istekte basılır ve
    /// noindex yazar; geçersiz değer yok sayılır (kayıtlı görünüm, noindex yok); 404 sayfası geçerli önizlemeyi taşır.
    /// Beklenen renkler setlerin elle yazılmış zeminleri (#f4f6f9 tarife/lacivert, #f7f6f5 vitrin/bordo).
    /// </summary>
    [Fact]
    public async Task Palet_ve_renk_onizlemesi_noindex_yazar_gecersiz_deger_yok_sayilir_404_tasir()
    {
        var tenant = await SeedTenantAsync(null); // kayıt yok → tarife, varsayılan set (petrol)
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        using (var r = await client.GetAsync("/?palet=lacivert"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Contains("<style>:root{--bg:#f4f6f9;", html);
            Assert.Contains("<meta name=\"theme-color\" content=\"#f4f6f9\" media=\"(prefers-color-scheme: light)\" />", html);
            Assert.Contains("<meta name=\"theme-color\" content=\"#10151d\" media=\"(prefers-color-scheme: dark)\" />", html);
            Assert.Equal("noindex, follow", RobotsHeader(r));
        }
        // Başka temanın seti: önizleme parametresi biçimce geçerli (noindex) ama bu temada yok → temanın varsayılan seti.
        using (var r = await client.GetAsync("/?palet=bordo"))
        {
            Assert.Contains("<style>:root{--bg:#f5f6f3;", await r.Content.ReadAsStringAsync());
            Assert.Equal("noindex, follow", RobotsHeader(r));
        }
        using (var r = await client.GetAsync("/?tema=vitrin&palet=bordo"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Matches(@"css/tema-vitrin\.[a-z0-9]+\.css", html);
            Assert.Contains("<style>:root{--bg:#f7f6f5;", html);
        }
        // Özel ana renk: yalnız KESİN #rrggbb (adreste %23); küçük harfle, Rgb'den yeniden yazılarak basılır.
        using (var r = await client.GetAsync("/?renk=%23C0392B"))
        {
            Assert.Contains("--brand:#c0392b;", await r.Content.ReadAsStringAsync());
            Assert.Equal("noindex, follow", RobotsHeader(r));
        }

        // Geçersiz değerler yok sayılır: varsayılan görünüm, noindex YOK, ham değer sayfaya sızmaz.
        foreach (var bad in new[]
                 {
                     "/?palet=olmayan", "/?palet=PETROL;}", "/?renk=c0392b", "/?renk=%23c0392b;}", "/?renk=red",
                     "/?renk=%23c0392b%3C%2Fstyle%3E", "/?palet=lacivert&palet=zeytin",
                 })
        {
            using var r = await client.GetAsync(bad);
            var html = await r.Content.ReadAsStringAsync();
            Assert.Contains("<style>:root{--bg:#f5f6f3;", html);
            Assert.Contains("--brand:#0b5d6b;", html);
            Assert.DoesNotContain("c0392b", html);
            Assert.True(RobotsHeader(r) is null, $"{bad}: {RobotsHeader(r)}");
        }

        // 404 sayfası geçerli tema + palet + renk'i taşır (diğer parametreler düşer); durum 404 kalır.
        using (var r = await client.GetAsync("/yok/boyle/bir/adres?tema=vitrin&palet=bordo&renk=%23c0392b&x=1"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Matches(@"css/tema-vitrin\.[a-z0-9]+\.css", html);
            Assert.Contains("<style>:root{--bg:#f7f6f5;", html);
            Assert.Contains("--brand:#c0392b;", html);
            Assert.Equal("noindex, follow", RobotsHeader(r));
        }
    }

    /// <summary>Kayıtlı set ve özel ana renk basılır; önizleme kaydı ezer; kayıtlı set temaya ait değilse (tema sonradan
    /// değişti — servis yalnız biçimi doğrular) temanın varsayılan seti basılır.</summary>
    [Fact]
    public async Task Kayitli_set_ve_ozel_renk_basilir_temaya_ait_olmayan_set_varsayilana_duser()
    {
        var tenant = await SeedTenantAsync("kontuar", "gok", "#c0392b");
        using (var factory = new PublicSiteFactory(fx, tenant))
        {
            var client = factory.Client();
            var html = await client.GetStringAsync("/");
            Assert.Matches(@"css/tema-kontuar\.[a-z0-9]+\.css", html);
            Assert.Contains("<style>:root{--bg:#f3f6fa;", html);
            Assert.Contains("--brand:#c0392b;", html); // #c0392b zeminde 3:1'i tutuyor → aynen
            using var r = await client.GetAsync("/?palet=terminal&renk=%230b1f3a");
            var preview = await r.Content.ReadAsStringAsync();
            Assert.Contains("<style>:root{--bg:#f4f7f9;", preview);
            Assert.Contains("--brand:#0b1f3a;", preview);
        }

        var stale = await SeedTenantAsync("tarife", "gok");
        using (var factory = new PublicSiteFactory(fx, stale))
            Assert.Contains("<style>:root{--bg:#f5f6f3;", await factory.Client().GetStringAsync("/"));
    }
}
