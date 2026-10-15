using System.Text.RegularExpressions;
using RentACar.Application.Fleet;
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
        // Beklenen anahtarlar elle (oracle): kayıt ve ayar listesi ikisi de buna eşit, aynı sırada.
        Assert.Equal(["pazar", "vitrin", "rota"], SiteThemeKeys.All);
        Assert.Equal(SiteThemeKeys.All, SiteThemes.All.Select(t => t.Key));
        Assert.Equal("pazar", SiteThemeKeys.Default);
        Assert.Equal("pazar", SiteThemes.Resolve(null).Key);
        Assert.Equal("pazar", SiteThemes.Resolve("olmayan").Key);
        Assert.Same(SiteThemes.Vitrin, SiteThemes.Resolve(" VITRIN "));
        foreach (var t in SiteThemes.All)
        {
            Assert.True(File.Exists(Path.Combine(CssDir(), ThemeFile(t))), t.CssPath);
            Assert.All(t.FontPreloads, f => Assert.Contains(t.Fonts, x => x.File == f));
        }
    }

    // Anahtar göçü: eski anahtarlar KALICI olarak yeni karşılığına normalize edilir (paylaşılmış ?tema= bağlantısı,
    // eski istemcinin PUT'u). Eski anahtar geçerli değer listesinde YOK — yeniden saklanamaz, ERP'de görünmez.
    [Theory]
    [InlineData("tarife", "pazar")]
    [InlineData(" TARIFE ", "pazar")]
    [InlineData("Tarife", "pazar")]
    [InlineData("kontuar", "rota")]
    [InlineData("KONTUAR", "rota")]
    [InlineData("pazar", "pazar")]
    [InlineData(" Rota", "rota")]
    [InlineData("vitrin", "vitrin")]
    public void Eski_anahtar_yeni_anahtara_normalize_edilir(string input, string expected)
    {
        Assert.Equal(expected, SiteThemeKeys.Normalize(input));
        Assert.True(SiteThemes.IsKnown(input));
        Assert.Equal(expected, SiteThemes.Resolve(input).Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("tarife2")]
    [InlineData("kontuar;}")]
    [InlineData("mavi")]
    public void Bilinmeyen_anahtar_normalize_edilmez(string? input)
    {
        Assert.Null(SiteThemeKeys.Normalize(input));
        Assert.False(SiteThemes.IsKnown(input));
        Assert.Equal("pazar", SiteThemes.Resolve(input).Key);
    }

    [Fact]
    public void Eski_anahtarlar_gecerli_listede_yok_ve_esleme_yalniz_gecerli_anahtara_gider()
    {
        Assert.Equal(new Dictionary<string, string> { ["tarife"] = "pazar", ["kontuar"] = "rota" },
            SiteThemeKeys.LegacyAliases.ToDictionary(x => x.Key, x => x.Value));
        Assert.DoesNotContain("tarife", SiteThemeKeys.All);
        Assert.DoesNotContain("kontuar", SiteThemeKeys.All);
        Assert.All(SiteThemeKeys.LegacyAliases.Values, v => Assert.Contains(v, SiteThemeKeys.All));
    }

    // Slot imzaları SÖZLEŞMEDİR (README): DynamicComponent eksik/yanlış adlı parametreyi ÇALIŞMA ANINDA reddeder.
    // Her temanın yedi slot bileşeni tam bu [Parameter] kümesini taşır — yeni tema PR'ı derlemede değil burada yakalanır.
    private static readonly Dictionary<string, (string Name, Type Type)[]> SlotParameters = new()
    {
        ["Hero"] =
        [
            ("Marka", typeof(string)), ("Branding", typeof(FleetBranding)),
            ("Subeler", typeof(IReadOnlyList<string>)), ("Kartlar", typeof(IReadOnlyList<FleetShowcaseCard>)),
            ("Sehir", typeof(string)),
        ],
        ["Card"] = [("Kart", typeof(ListingCard)), ("Tarih", typeof(StayDates))],
        ["Header"] = [("Branding", typeof(FleetBranding)), ("Nav", typeof(IReadOnlyList<SiteLink>))],
        ["Footer"] =
        [
            ("Branding", typeof(FleetBranding)), ("Sayfalar", typeof(IReadOnlyList<SiteLink>)),
            ("YasalSayfalar", typeof(IReadOnlyList<SiteLink>)),
        ],
        ["Home"] = [("Model", typeof(HomeModel))],
        ["Fleet"] = [("Model", typeof(FleetModel))],
        ["DetailHero"] = [("Model", typeof(DetailHeroModel))],
    };

    [Theory]
    [MemberData(nameof(Themes))]
    public void Tema_yedi_slotu_sozlesme_imzalariyla_tasir(string key)
    {
        var theme = SiteThemes.Resolve(key);
        var slots = new Dictionary<string, Type>
        {
            ["Hero"] = theme.Hero, ["Card"] = theme.Card, ["Header"] = theme.Header, ["Footer"] = theme.Footer,
            ["Home"] = theme.Home, ["Fleet"] = theme.Fleet, ["DetailHero"] = theme.DetailHero,
        };
        Assert.Equal(SlotParameters.Keys.Order(), slots.Keys.Order());
        foreach (var (slot, type) in slots)
        {
            Assert.True(typeof(Microsoft.AspNetCore.Components.IComponent).IsAssignableFrom(type), $"{key} {slot}: bileşen değil");
            var declared = type.GetProperties()
                .Where(p => p.IsDefined(typeof(Microsoft.AspNetCore.Components.ParameterAttribute), inherit: true))
                .Select(p => (p.Name, p.PropertyType))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();
            var expected = SlotParameters[slot].OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
            Assert.True(expected.SequenceEqual(declared),
                $"{key} {slot} ({type.Name}) parametreleri: " + string.Join(", ", declared.Select(d => $"{d.Name}:{d.PropertyType.Name}")));
        }

        // Her tema KENDİ bileşenlerini taşır (başka temanın bileşenine düşmek dağıtımı test etmez).
        Assert.Equal(7, slots.Values.Distinct().Count());
        Assert.All(SiteThemes.All.Where(t => t != theme), other =>
            Assert.Empty(slots.Values.Intersect([other.Hero, other.Card, other.Header, other.Footer, other.Home, other.Fleet, other.DetailHero])));
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
