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
/// yalnız palet değerlerinin kendisi SitePaletteCss'ten gelir, AA'ları ayrıca bağımsız hesapla doğrulanır —
/// palet üreticisinin kendi değişmezleri SitePaletteCssTests'te).
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
        // Palet bloğunun şemadan bağımsız sabitleri — tema css'i yedeklerini tanımlar.
        "--day-bg", "--day-ink", "--night-bg", "--night-ink",
    ];

    /// <summary>
    /// Primitive'e (<c>var(--p-…)</c>) bağlanabilen token'lar: palet bloğunun bastığı token'ların YEDEKLERİ (blok
    /// basılamazsa site aynı görünsün) + gerekçeli muafiyetler. Geri kalan her token ve her slot kuralı semantik
    /// token'a bağlanır — palet her şeyi değiştirsin.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> PrimitiveExemptions = new Dictionary<string, string>
    {
        ["--danger"] = "durum rengi (hata) palete bağlı değil; her setin zemininde AA ayrıca ölçülür",
        ["--ok"] = "durum rengi (başarı) palete bağlı değil; her setin zemininde AA ayrıca ölçülür",
        ["--shadow-1"] = "koyu modda gölge saf siyah (palete bağlı değil); açık modda mürekkep tonu",
        ["--shadow-2"] = "koyu modda gölge saf siyah (palete bağlı değil); açık modda mürekkep tonu",
        ["--ticket-drop"] = "koyu modda kupon gölgesi saf siyah (palete bağlı değil)",
        ["--mask-on"] = "maske yalnız alfa kanalı taşır, görünen renk değil",
        ["--mask-off"] = "maske yalnız alfa kanalı taşır, görünen renk değil",
        ["--wa-bg"] = "WhatsApp düğmesi markanın sabit koyu yeşili (palete bağlı değil); beyaz yazıyla AA ayrıca ölçülür",
        ["--wa-ink"] = "WhatsApp düğmesinin yazısı sabit beyaz; --wa-bg üstünde AA ayrıca ölçülür",
        ["--rozet-iyi-bg"] = "Kıyas olumlu rozeti anlam rengi (yeşil), palete bağlı değil; yazısıyla AA ayrıca ölçülür",
        ["--rozet-iyi-ink"] = "Kıyas olumlu rozeti yazısı; zemininde AA ayrıca ölçülür",
        ["--rozet-dikkat-bg"] = "Kıyas dikkat rozeti anlam rengi (kehribar), palete bağlı değil; yazısıyla AA ayrıca ölçülür",
        ["--rozet-dikkat-ink"] = "Kıyas dikkat rozeti yazısı; zemininde AA ayrıca ölçülür",
    };

    /// <summary>Sabit renk çiftleri (palete bağlı değil): WhatsApp düğmesi (#25d366 üstünde beyaz 1,98:1 geçmezdi —
    /// araştırma) ve Kıyas rozetleri; yazı/zemin 4.5:1'i iki şemada da tutar. Beklenen eşik elle; renkler css'ten.</summary>
    [Theory]
    [InlineData("anahtar", "--wa-ink", "--wa-bg")]
    [InlineData("kiyas", "--wa-ink", "--wa-bg")]
    [InlineData("kiyas", "--rozet-iyi-ink", "--rozet-iyi-bg")]
    [InlineData("kiyas", "--rozet-dikkat-ink", "--rozet-dikkat-bg")]
    public void Sabit_renk_ciftleri_AA(string key, string ink, string ground)
    {
        var css = ReadCss(ThemeFile(SiteThemes.Resolve(key)));
        foreach (var dark in new[] { false, true })
        {
            var map = Tokens(css, dark);
            var c = Contrast(ResolveHex(map, ink), ResolveHex(map, ground));
            Assert.True(c >= 4.5, $"{key} {(dark ? "koyu" : "açık")}: {ink}/{ground} = {c:0.00}");
        }
        Assert.NotEqual("#25d366", ResolveHex(Tokens(css, false), ground));
    }

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
        Assert.Equal(["anahtar", "kiyas", "pazar", "vitrin", "rota"], SiteThemeKeys.All);
        Assert.Equal(SiteThemeKeys.All, SiteThemes.All.Select(t => t.Key));
        Assert.Equal("anahtar", SiteThemeKeys.Default);
        Assert.Equal("anahtar", SiteThemes.Resolve(null).Key);
        Assert.Equal("anahtar", SiteThemes.Resolve("olmayan").Key);
        Assert.Same(SiteThemes.Pazar, SiteThemes.Resolve("pazar")); // kayıtlı eski tema varsayılana düşmez
        Assert.Same(SiteThemes.Vitrin, SiteThemes.Resolve(" VITRIN "));
        foreach (var t in SiteThemes.All)
        {
            Assert.True(File.Exists(Path.Combine(CssDir(), ThemeFile(t))), t.CssPath);
            Assert.All(t.FontPreloads, f => Assert.Contains(t.Fonts, x => x.File == f));
        }
    }

    [GeneratedRegex("^[a-z0-9-]{1,30}$")]
    private static partial Regex PaletteKeyFormat();

    /// <summary>PublicSite palet setleri ↔ Application <see cref="SitePaletteKeys"/> (API aitlik doğrulaması + ERP
    /// kartları) BİREBİR: aynı anahtarlar aynı sırada (ilk = varsayılan), aynı ad, kart çipleri setin renkleri.</summary>
    [Fact]
    public void Palet_setleri_ve_ayar_anahtarlari_ayni()
    {
        Assert.Equal(SiteThemeKeys.All.Order(StringComparer.Ordinal), SitePaletteKeys.ByTheme.Keys.Order(StringComparer.Ordinal));
        foreach (var theme in SiteThemes.All)
        {
            Assert.NotEmpty(theme.Palettes);
            Assert.Same(theme.Palettes[0], theme.DefaultPalette);
            Assert.Equal(theme.Palettes.Count, theme.Palettes.Select(p => p.Key).Distinct().Count());
            var options = SitePaletteKeys.For(theme.Key);
            Assert.Equal(theme.Palettes.Select(p => p.Key), options.Select(o => o.Key));
            foreach (var (p, o) in theme.Palettes.Zip(options))
            {
                Assert.Matches(PaletteKeyFormat(), p.Key);
                Assert.Equal(p.Key, SitePaletteKeys.Normalize(p.Key));
                var expected = new SitePaletteOption(p.Key, p.Ad, p.Light.Bg, p.Light.Ink, p.Light.Primary, p.Light.Accent,
                    p.Dark.Bg);
                Assert.True(expected == o, $"{theme.Key}/{p.Key}: SitePaletteKeys satırı {expected} olmalı, {o} yazılı");
                foreach (var hex in new[] { p.Light.Bg, p.Light.Surface, p.Light.Ink, p.Light.Primary, p.Dark.Bg,
                             p.Dark.Surface, p.Dark.Ink, p.Dark.Primary })
                    Assert.Matches("^#[0-9a-f]{6}$", hex);
            }
        }
    }

    [GeneratedRegex(@"var\(--p-[\w-]+\)")]
    private static partial Regex PrimitiveRef();

    /// <summary>
    /// Primitive bloğundan SONRA <c>var(--p-…)</c> yalnız <c>:root</c>'taki palet token yedeklerinde
    /// (<see cref="SitePaletteCss.SchemeTokenNames"/> + <see cref="SitePaletteCss.FixedTokenNames"/>) ya da gerekçeli
    /// muafiyetlerde (<see cref="PrimitiveExemptions"/>) geçebilir. Slot kuralları (<c>.marka img</c> …) ve bileşen
    /// token'ları (<c>--board-*</c>, <c>--stage-*</c>, <c>--logo-plate</c>, <c>--field-bg</c>, <c>--line-soft</c> …)
    /// primitive'e doğrudan bağlanırsa palet onları değiştiremezdi.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void Primitive_yalniz_palet_yedeklerinde_ve_gerekceli_muafiyetlerde(string key)
    {
        var allowed = SitePaletteCss.SchemeTokenNames.Concat(SitePaletteCss.FixedTokenNames)
            .Concat(PrimitiveExemptions.Keys).ToHashSet(StringComparer.Ordinal);
        var violations = PrimitiveUses(ReadCss(ThemeFile(SiteThemes.Resolve(key))))
            .Where(u => u.Selector != ":root" || !allowed.Contains(u.Property))
            .Select(u => $"{u.Selector} {{ {u.Property}: …{u.Reference} }}")
            .ToList();
        Assert.True(violations.Count == 0, $"{key}: primitive'e doğrudan bağlı: " + string.Join(" | ", violations));
    }

    /// <summary>İlk (primitive) <c>:root</c> bloğundan sonraki her <c>var(--p-…)</c>: hangi seçicide, hangi özellikte.</summary>
    private static List<(string Selector, string Property, string Reference)> PrimitiveUses(string css)
    {
        var start = css.IndexOf(":root", StringComparison.Ordinal);
        var rest = css[(css.IndexOf('}', start) + 1)..];
        var uses = new List<(string, string, string)>();
        foreach (Match m in PrimitiveRef().Matches(rest))
        {
            var declStart = Math.Max(rest.LastIndexOf('{', m.Index), rest.LastIndexOf(';', m.Index)) + 1;
            var property = rest[declStart..m.Index].Split(':')[0].Trim();
            var open = rest.LastIndexOf('{', m.Index);
            var selStart = Math.Max(rest.LastIndexOf('}', open), rest.LastIndexOf('{', open - 1)) + 1;
            uses.Add((rest[selStart..open].Trim(), property, m.Value));
        }
        return uses;
    }

    /// <summary>Kendi kendini denetler: tarayıcı gerçekten seçiciyi ve özelliği buluyor mu (slot kuralı, bileşen
    /// token'ı, koyu bloktaki semantik token).</summary>
    [Fact]
    public void Primitive_tarayicisi_secici_ve_ozelligi_bulur()
    {
        const string css = ":root{--p-a:#fff}:root{--bg:var(--p-a);--board-bg:var(--p-a)}.x{background:var(--p-a)}"
                           + "@media (prefers-color-scheme: dark){:root{--focus:var(--p-a);--danger:var(--p-a)}}";
        Assert.Equal(
            [(":root", "--bg"), (":root", "--board-bg"), (".x", "background"), (":root", "--focus"), (":root", "--danger")],
            PrimitiveUses(css).Select(u => (u.Selector, u.Property)));
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
        Assert.Equal("anahtar", SiteThemes.Resolve(input).Key);
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
