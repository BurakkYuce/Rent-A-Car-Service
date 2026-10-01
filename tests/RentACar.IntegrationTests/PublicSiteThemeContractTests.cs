using System.Text.RegularExpressions;
using RentACar.Application.TenantSettings;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.PublicSite;
using RentACar.PublicSite.Components.Themes;

namespace RentACar.IntegrationTests;

/// <summary>
/// Site tema sözleşmesi — YAPISAL testler (css dosyaları kaynaktan okunur; Components/Themes/README.md).
/// Kontrast beklentisi WCAG 2.x formülünün testteki BAĞIMSIZ uygulamasıyla ölçülür (üretim kodu çağrılmaz;
/// yalnız firma rengi paletinin kendisi BrandPalette'ten gelir, onun AA'sı ayrıca bağımsız hesapla doğrulanır).
/// </summary>
[Collection("postgres")]
public sealed partial class PublicSiteThemeContractTests(PostgresFixture fx)
{
    public static readonly string[] SemanticTokens =
    [
        "--bg", "--bg-alt", "--surface", "--surface-raised", "--ink", "--ink-muted", "--line", "--brand",
        "--brand-ink", "--brand-text", "--accent", "--accent-ink", "--focus", "--danger", "--ok",
        "--font-display", "--font-body", "--step--1", "--step-0", "--step-1", "--step-2", "--step-3",
        "--step-4", "--step-5", "--space-1", "--space-2", "--space-3", "--space-4", "--space-5", "--space-6",
        "--space-7", "--space-8", "--radius-s", "--radius-m", "--radius-l", "--shadow-1", "--shadow-2", "--measure",
        // base.css'in ayrıca kullandıkları (README "base.css ayrıca şunları kullanır")
        "--ink-subtle", "--line-strong", "--on-brand", "--brand-soft",
    ];

    public static readonly string[] ComponentTokens =
    [
        "--btn-bg", "--btn-ink", "--btn-radius", "--card-bg", "--card-radius", "--card-border",
        "--field-bg", "--field-border", "--price-ink", "--price-mark",
    ];

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(")]
    private static partial Regex ColorLiteral();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"@import\s+url\(""([^""]+)""\)\s*;")]
    private static partial Regex Import();

    private static string CssDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "src", "RentACar.PublicSite", "wwwroot", "css");
    }

    /// <summary>Yorumsuz css; <c>@import url("x.css")</c> satırları aynı klasörden içeriğe açılır.</summary>
    private static string ReadCss(string file)
    {
        var text = Comment().Replace(File.ReadAllText(Path.Combine(CssDir(), file)), "");
        return Import().Replace(text, m => ReadCss(m.Groups[1].Value));
    }

    private static string ThemeFile(SiteTheme t) => Path.GetFileName(t.CssPath);

    public static TheoryData<string> Themes() => [.. SiteThemes.All.Select(t => t.Key)];

    [Fact]
    public void Kayit_ve_ayar_anahtarlari_ayni()
    {
        Assert.Equal(SiteThemeKeys.All, SiteThemes.All.Select(t => t.Key));
        Assert.Same(SiteThemes.Tarife, SiteThemes.Resolve(null));
        Assert.Same(SiteThemes.Tarife, SiteThemes.Resolve("olmayan"));
        Assert.Same(SiteThemes.Vitrin, SiteThemes.Resolve(" VITRIN "));
        foreach (var t in SiteThemes.All)
        {
            Assert.True(File.Exists(Path.Combine(CssDir(), ThemeFile(t))), t.CssPath);
            Assert.All(t.FontPreloads, f => Assert.Contains(t.Fonts, x => x.File == f));
        }
    }

    [Fact]
    public void Base_css_renk_literali_icermez()
    {
        var hits = ColorLiteral().Matches(ReadCss("base.css")).Select(m => m.Value).ToList();
        Assert.True(hits.Count == 0, "base.css renk literali: " + string.Join(", ", hits));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Tema_tum_tokenlari_tanimlar_ve_literaller_yalniz_primitive_blokta(string key)
    {
        var css = ReadCss(ThemeFile(SiteThemes.Resolve(key)));
        foreach (var token in SemanticTokens.Concat(ComponentTokens))
            Assert.True(Regex.IsMatch(css, Regex.Escape(token) + @"\s*:"), $"{key}: {token} tanımlı değil");

        // İlk :root bloğu primitive; ondan SONRA renk literali yok.
        var start = css.IndexOf(":root", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{key}: :root yok");
        var end = css.IndexOf('}', start);
        var rest = css[(end + 1)..];
        var hits = ColorLiteral().Matches(rest).Select(m => m.Value).ToList();
        Assert.True(hits.Count == 0, $"{key}: primitive bloğu dışında renk literali: " + string.Join(", ", hits));
    }

    // Vitrin sahnesindeki model adı bağlantısı koyulaşan fotoğraf / grafit sahne üstünde durur: genel --focus
    // (açık modda grafit) orada ~1:1 kalıyordu. Kural: bu seçicinin kendi :focus-visible kuralı var, açık dış
    // halka (sahne mürekkebi) + koyu iç halka (sahne gölgesi) — ikisi birlikte her zeminde görünür.
    [Fact]
    public void Vitrin_sahne_model_baglantisi_cift_odak_halkasi_tasir()
    {
        var css = ReadCss(ThemeFile(SiteThemes.Vitrin));
        var m = Regex.Match(css, @"\.v-sahne\s+a\.v-model:focus-visible\s*\{(?<body>[^}]*)\}");
        Assert.True(m.Success, "tema-vitrin.css: .v-sahne a.v-model:focus-visible kuralı yok");
        var body = m.Groups["body"].Value;
        Assert.Matches(@"outline\s*:[^;]*var\(--stage-ink\)", body);
        Assert.Matches(@"box-shadow\s*:[^;]*var\(--stage-shade\)", body);
        Assert.DoesNotContain("--focus", body);
    }
}
