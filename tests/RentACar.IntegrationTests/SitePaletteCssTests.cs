using System.Text.RegularExpressions;
using RentACar.PublicSite;
using RentACar.PublicSite.Components.Themes;

namespace RentACar.IntegrationTests;

/// <summary>
/// Halka açık site palet üreticisi (<see cref="SitePaletteCss"/>) — saf birim testi (DB yok).
///
/// <para>BAĞIMSIZ ORACLE: kontrast, üretim kodunu ÇAĞIRMAYAN ayrı bir WCAG 2.x hesabıyla (<see cref="Contrast"/>)
/// ölçülür; formülün kendisi yayımlanmış referanslarla (siyah/beyaz 21:1, #767676 beyazda 4.54:1) kilitli.
/// "Varsayılan set bugünkü görünümü korur" beklentileri koddan değil, değişiklikten ÖNCE canlı sitenin
/// bastığı değerlerden (eski BrandPalette çıktısı ve tema css primitive'leri) elle yazıldı.</para>
/// </summary>
public sealed partial class SitePaletteCssTests
{
    // ---- Bağımsız WCAG kontrast hesabı (üretim kodunu çağırmaz) ----
    private static double Lum(string hex)
    {
        static double Ch(string h)
        {
            var c = Convert.ToInt32(h, 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(hex.Substring(1, 2)) + 0.7152 * Ch(hex.Substring(3, 2)) + 0.0722 * Ch(hex.Substring(5, 2));
    }

    private static double Contrast(string a, string b)
    {
        var (x, y) = (Lum(a), Lum(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static readonly string[] Grid = BuildGrid();

    /// <summary>6×6×6 renk ızgarası (216 renk; siyah, beyaz, doygun uçlar, orta griler dahil).</summary>
    private static string[] BuildGrid()
    {
        string[] steps = ["00", "33", "66", "99", "cc", "ff"];
        return [.. from r in steps from g in steps from b in steps select $"#{r}{g}{b}"];
    }

    public static TheoryData<string, string> ThemePalettes()
    {
        var data = new TheoryData<string, string>();
        foreach (var t in SiteThemes.All)
            foreach (var p in t.Palettes)
                data.Add(t.Key, p.Key);
        return data;
    }

    /// <summary>Bir şemanın TÜM AA çiftleri (metin 4.5:1, dolgu/bileşen 3:1).</summary>
    private static void AssertScheme(SchemeTokens s, string label)
    {
        void Text(Rgb fg, Rgb bg, string pair)
            => Assert.True(Contrast(fg.Hex, bg.Hex) >= 4.5, $"{label} {pair}: {fg.Hex}/{bg.Hex} = {Contrast(fg.Hex, bg.Hex):0.00}");
        void Ui(Rgb fg, Rgb bg, string pair)
            => Assert.True(Contrast(fg.Hex, bg.Hex) >= 3.0, $"{label} {pair}: {fg.Hex}/{bg.Hex} = {Contrast(fg.Hex, bg.Hex):0.00}");

        Text(s.Ink, s.Bg, "ink/bg");
        Text(s.Ink, s.Surface, "ink/surface");
        Text(s.Ink, s.BgAlt, "ink/bg-alt");
        Text(s.Ink, s.SurfaceRaised, "ink/surface-raised");
        Text(s.InkMuted, s.Bg, "ink-muted/bg");
        Text(s.InkMuted, s.Surface, "ink-muted/surface");
        Text(s.InkMuted, s.BgAlt, "ink-muted/bg-alt");
        Text(s.InkSubtle, s.Bg, "ink-subtle/bg");
        Text(s.InkSubtle, s.Surface, "ink-subtle/surface");
        Text(s.InkSubtle, s.BgAlt, "ink-subtle/bg-alt");
        Text(s.BrandText, s.Bg, "brand-text/bg");
        Text(s.BrandText, s.Surface, "brand-text/surface");
        Text(s.BrandText, s.BrandSoft, "brand-text/brand-soft");
        Text(s.OnBrand, s.Brand, "on-brand/brand");
        Text(s.AccentInk, s.Accent, "accent-ink/accent");
        Ui(s.Brand, s.Bg, "brand/bg (dolgu)");
        Ui(s.Brand, s.Surface, "brand/surface (dolgu)");
    }

    [Fact]
    public void Kontrast_formulu_yayimlanmis_referanslarla_ayni()
    {
        Assert.Equal(21.0, Rgb.Contrast(Rgb.Black, Rgb.White), 3);
        // WCAG örneklerinde AA sınırı olarak geçen gri: beyaz üstünde 4.54:1.
        Assert.Equal(4.54, Rgb.Contrast(new(0x76, 0x76, 0x76), Rgb.White), 2);
        Assert.Equal(1.0, Rgb.Contrast(new(10, 20, 30), new(10, 20, 30)), 6);
        Assert.Equal(Contrast("#0b5d6b", "#f5f6f3"), Rgb.Contrast(Rgb.Parse("#0b5d6b"), Rgb.Parse("#f5f6f3")), 9);
    }

    [Theory]
    [MemberData(nameof(ThemePalettes))]
    public void Her_tema_her_set_iki_semada_AA(string theme, string palette)
    {
        var t = SitePaletteCss.Tokens(SiteThemes.Resolve(theme), palette, null);
        Assert.Equal(palette, t.Palette.Key);
        AssertScheme(t.Light, $"{theme}/{palette} açık");
        AssertScheme(t.Dark, $"{theme}/{palette} koyu");
        // Şemadan bağımsız sabitler: koyu levha (gündüz mürekkebi) üstünde gece mürekkebi okunur (kontuar panosu).
        Assert.True(Contrast(t.NightInk.Hex, t.DayInk.Hex) >= 4.5, $"{theme}/{palette}: night-ink/day-ink");
        Assert.True(Contrast(t.DayInk.Hex, t.DayBg.Hex) >= 4.5, $"{theme}/{palette}: day-ink/day-bg");
        Assert.True(Contrast(t.NightInk.Hex, t.NightBg.Hex) >= 4.5, $"{theme}/{palette}: night-ink/night-bg");
        // Açık şema açık, koyu şema koyu (tema css'inin color-scheme bildirimiyle tutarlı).
        Assert.True(Lum(t.Light.Bg.Hex) > Lum(t.Light.Ink.Hex), $"{theme}/{palette}: açık şemanın zemini mürekkepten açık");
        Assert.True(Lum(t.Dark.Bg.Hex) < Lum(t.Dark.Ink.Hex), $"{theme}/{palette}: koyu şemanın zemini mürekkepten koyu");
    }

    /// <summary>Firmanın kendi ana rengi: 216 renk × her temanın her seti × iki şema. Her renkte AA değişmezleri tutar;
    /// zemin ve kartla zaten 3:1 veren renk dolguda AYNEN korunur.</summary>
    [Fact]
    public void Ozel_ana_renk_216_renk_izgarasi_her_temanin_her_setinde_AA()
    {
        foreach (var theme in SiteThemes.All)
        foreach (var palette in theme.Palettes)
        foreach (var hex in Grid)
        {
            var t = SitePaletteCss.Tokens(palette, hex);
            foreach (var (s, mode) in new[] { (t.Light, "açık"), (t.Dark, "koyu") })
            {
                var label = $"{theme.Key}/{palette.Key} {mode} {hex}";
                AssertScheme(s, label);
                if (Contrast(hex, s.Bg.Hex) >= 3.0 && Contrast(hex, s.Surface.Hex) >= 3.0)
                    Assert.True(hex == s.Brand.Hex, $"{label}: yeterli renk korunmalı, {s.Brand.Hex} basıldı");
            }
        }
    }

    /// <summary>Varsayılan setler: vitrin ve kontuar değişiklikten ÖNCE canlı sitenin bastığı değerleri (eski
    /// BrandPalette çıktısı + tema css primitive'leri) korur. Kontuar'da dolgu üstü yazı artık temanın kendi laciverti
    /// (#16263a; tema yorumunun niyeti). Pazar yeni tema (eski tarife'nin yerine): zemin/mürekkep/çizgi değerleri
    /// tasarımda elle seçildi; türetilen ana renk tonları üretim kodunu ÇAĞIRMAYAN ayrı bir hesapla bulundu ve elle
    /// doğrulanabilir — ör. açık hafif ton = zemin #edf0f2 ile #d42a1c'nin %12 karışımı: (237−25·0.12, 240−198·0.12,
    /// 242−214·0.12) → #ead8d8; #d42a1c zeminde 4.4:1, beyazda 5.1:1 (dolgu aynen kalır, üstünde beyaz).</summary>
    [Fact]
    public void Varsayilan_setler_bugunku_renkleri_korur()
    {
        var pazar = SitePaletteCss.Tokens(SiteThemes.Pazar, null, null);
        Assert.Equal("domates", pazar.Palette.Key);
        AssertHex(pazar.Light, bg: "#edf0f2", bgAlt: "#e1e5e9", surface: "#ffffff", ink: "#1b1d22", muted: "#454a53",
            subtle: "#5a5f69", line: "#c8cdd3", lineStrong: "#8b919a", accent: "#ffc928", accentInk: "#1b1d22");
        AssertBrand(pazar.Light, "#d42a1c", "#ffffff", "#bb2519", "#ead8d8");
        AssertHex(pazar.Dark, bg: "#16171a", bgAlt: "#1c1d21", surface: "#1f2125", ink: "#eef0f2", muted: "#bcc1c8",
            subtle: "#9ca2ab", line: "#32353b", lineStrong: "#50545c", accent: "#ffc928", accentInk: "#16171a");
        AssertBrand(pazar.Dark, "#d13a28", "#ffffff", "#dd6d60", "#3f1f1d");
        Assert.Equal(("#edf0f2", "#1b1d22", "#16171a", "#eef0f2"),
            (pazar.DayBg.Hex, pazar.DayInk.Hex, pazar.NightBg.Hex, pazar.NightInk.Hex));

        var vitrin = SitePaletteCss.Tokens(SiteThemes.Vitrin, null, null);
        Assert.Equal("grafit", vitrin.Palette.Key);
        AssertHex(vitrin.Light, bg: "#f7f7f5", bgAlt: "#eeefec", surface: "#ffffff", ink: "#2a3036", muted: "#50575e",
            subtle: "#62696f", line: "#c5cbd1", lineStrong: "#858d95", accent: "#0b5d6b", accentInk: "#ffffff");
        Assert.Equal("#0b5d6b", vitrin.Light.Brand.Hex);
        Assert.Equal("#0b5d6b", vitrin.Light.BrandText.Hex);
        AssertHex(vitrin.Dark, bg: "#14171a", bgAlt: "#1b1f23", surface: "#1b1f23", ink: "#eef0f1", muted: "#c9ced3",
            subtle: "#a3aab1", line: "#2a3036", lineStrong: "#62696f", accent: "#2d7480", accentInk: "#ffffff");
        Assert.Equal("#2d7480", vitrin.Dark.Brand.Hex);

        var kontuar = SitePaletteCss.Tokens(SiteThemes.Kontuar, null, null);
        Assert.Equal("terminal", kontuar.Palette.Key);
        AssertHex(kontuar.Light, bg: "#f4f7f9", bgAlt: "#e7edf2", surface: "#ffffff", ink: "#16263a", muted: "#3b4d63",
            subtle: "#52647a", line: "#cfd8e0", lineStrong: "#8a98a7", accent: "#ff6a13", accentInk: "#16263a");
        Assert.Equal("#eb6211", kontuar.Light.Brand.Hex);     // turuncu zeminde 3:1'e koyulaştırıldı (eskisiyle aynı)
        Assert.Equal("#ad480d", kontuar.Light.BrandText.Hex);
        Assert.Equal("#16263a", kontuar.Light.OnBrand.Hex);
        AssertHex(kontuar.Dark, bg: "#0a131d", bgAlt: "#0f1925", surface: "#131d2b", ink: "#e9eff5", muted: "#a9b8c8",
            subtle: "#a9b8c8", line: "#24344a", lineStrong: "#3a5070", accent: "#ff6a13", accentInk: "#0a131d");
        Assert.Equal("#ff6a13", kontuar.Dark.Brand.Hex);
        Assert.Equal("#ff6a13", kontuar.Dark.BrandText.Hex);
        Assert.Equal("#16263a", kontuar.Dark.OnBrand.Hex);
        Assert.Equal(("#f4f7f9", "#16263a", "#0a131d", "#e9eff5"),
            (kontuar.DayBg.Hex, kontuar.DayInk.Hex, kontuar.NightBg.Hex, kontuar.NightInk.Hex));
    }

    private static void AssertHex(SchemeTokens s, string bg, string bgAlt, string surface, string ink, string muted,
        string subtle, string line, string lineStrong, string accent, string accentInk)
    {
        Assert.Equal(
            (bg, bgAlt, surface, ink, muted, subtle, line, lineStrong, accent, accentInk),
            (s.Bg.Hex, s.BgAlt.Hex, s.Surface.Hex, s.Ink.Hex, s.InkMuted.Hex, s.InkSubtle.Hex, s.Line.Hex,
                s.LineStrong.Hex, s.Accent.Hex, s.AccentInk.Hex));
    }

    private static void AssertBrand(SchemeTokens s, string brand, string onBrand, string text, string soft)
        => Assert.Equal((brand, onBrand, text, soft), (s.Brand.Hex, s.OnBrand.Hex, s.BrandText.Hex, s.BrandSoft.Hex));

    [Fact]
    public void Sari_ana_renk_acikta_dolguda_ve_metinde_koyulastirilir_koyuda_aynen_kalir()
    {
        var t = SitePaletteCss.Tokens(SiteThemes.Pazar, null, "#ffd400");
        // Sarı açık zeminle ~1.3:1 — düğme sayfada kaybolurdu (WCAG 1.4.11) → dolgu koyulaştırıldı.
        Assert.NotEqual("#ffd400", t.Light.Brand.Hex);
        Assert.True(Contrast(t.Light.Brand.Hex, "#edf0f2") >= 3.0);
        Assert.Equal("#1b1d22", t.Light.OnBrand.Hex); // temanın mürekkebi koyulaşmış sarı üstünde
        Assert.True(Contrast("#1b1d22", t.Light.Brand.Hex) >= 4.5);
        Assert.NotEqual("#ffd400", t.Light.BrandText.Hex);
        Assert.True(Contrast(t.Light.BrandText.Hex, "#edf0f2") >= 4.5);
        // Sarı koyu zeminde zaten okunuyor → koyu şemada dolgu ve metin aynen kalır.
        Assert.Equal("#ffd400", t.Dark.Brand.Hex);
        Assert.Equal("#ffd400", t.Dark.BrandText.Hex);
    }

    [Fact]
    public void Lacivert_ana_renk_acikta_aynen_koyuda_acilir()
    {
        var t = SitePaletteCss.Tokens(SiteThemes.Pazar, null, "#0b1f3a");
        Assert.Equal("#0b1f3a", t.Light.Brand.Hex);
        Assert.Equal("#0b1f3a", t.Light.BrandText.Hex);
        Assert.Equal("#ffffff", t.Light.OnBrand.Hex);
        Assert.NotEqual("#0b1f3a", t.Dark.Brand.Hex); // koyu zeminde düğme kaybolurdu
        Assert.True(Contrast(t.Dark.Brand.Hex, "#16171a") >= 3.0);
        Assert.True(Contrast(t.Dark.Brand.Hex, "#1f2125") >= 3.0);
    }

    /// <summary>Orta gri (#808080): beyaz 3.95:1, pazar mürekkebi (#1b1d22) 4.27:1 — ikisi de AA'nın altında. Elle
    /// hesap: gri L=0.216 → saf siyah (0.216+0.05)/0.05 = 5.32:1 → siyah seçilmeli.</summary>
    [Fact]
    public void Orta_tonda_ne_beyaz_ne_murekkep_yeterse_saf_siyah_secilir()
    {
        Assert.Equal("#000000", SitePaletteCss.Tokens(SiteThemes.Pazar, null, "#808080").Light.OnBrand.Hex);
        Assert.True(Contrast("#1b1d22", "#808080") < 4.5); // senaryonun kendisi: mürekkep yetmiyor
    }

    /// <summary>Elle ayar da güvenceden geçer: beyaz kart üstünde #cccccc ipucu metni (1.6:1) ve vurgu üstünde vurgunun
    /// kendi rengindeki yazı (1:1) BASILMAZ — kaydırılmış/seçilmiş değer gelir.</summary>
    [Fact]
    public void Kontrasti_tutmayan_elle_ayar_duzeltilir()
    {
        var weak = new SitePalette("deneme", "Deneme",
            new SchemeColors("#ffffff", "#ffffff", "#111111", "#0b5d6b", "#f2c230")
            {
                InkMuted = "#cccccc", InkSubtle = "#dddddd", AccentInk = "#f2c230",
            },
            new SchemeColors("#111111", "#1a1a1a", "#eeeeee", "#0b5d6b", "#f2c230") { InkMuted = "#333333" });
        var t = SitePaletteCss.Tokens(weak, null);
        Assert.NotEqual("#cccccc", t.Light.InkMuted.Hex);
        Assert.NotEqual("#dddddd", t.Light.InkSubtle.Hex);
        Assert.NotEqual("#f2c230", t.Light.AccentInk.Hex);
        Assert.NotEqual("#333333", t.Dark.InkMuted.Hex);
        AssertScheme(t.Light, "deneme açık");
        AssertScheme(t.Dark, "deneme koyu");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#12345g")]
    [InlineData("#1234567")]
    [InlineData("12345678")]
    [InlineData("#-12345")]
    [InlineData("#12345\0")]
    [InlineData("#fff;}</style><script>")]
    [InlineData(" #0b5d6b")]
    public void Bicimsiz_ozel_renk_yok_sayilir_ve_CSSe_sizmaz(string? value)
    {
        foreach (var theme in SiteThemes.All)
        {
            var css = SitePaletteCss.Build(theme, null, value);
            Assert.Equal(SitePaletteCss.Build(theme, null, null), css);
            Assert.DoesNotContain("<", css);
            Assert.DoesNotContain("script", css);
        }
    }

    [Theory]
    [InlineData(null, "domates")]
    [InlineData("", "domates")]
    [InlineData("olmayan", "domates")]
    [InlineData("bordo", "domates")]                // vitrin'in seti → pazar'da varsayılan
    [InlineData("petrol", "domates")]               // eski tarife'nin seti artık yok → varsayılan
    [InlineData("domates;}</style><script>", "domates")]
    [InlineData(" Kobalt ", "kobalt")]
    [InlineData("patlican", "patlican")]
    public void Palet_anahtari_temanin_setlerinde_aranir_yoksa_varsayilan(string? key, string expected)
    {
        Assert.Equal(expected, SitePaletteCss.Resolve(SiteThemes.Pazar, key).Key);
        var css = SitePaletteCss.Build(SiteThemes.Pazar, key, null);
        Assert.Equal(SitePaletteCss.Build(SiteThemes.Pazar, expected, null), css);
        Assert.DoesNotContain("<", css);
    }

    [GeneratedRegex(@"(--[a-z-]+):([^;}]*)")]
    private static partial Regex CssDeclaration();

    [Fact]
    public void Css_iki_semayi_tum_tokenlarla_ve_yalniz_hex_degerle_yazar()
    {
        var css = SitePaletteCss.Build(SiteThemes.Kontuar, "gok", "#C0392B"); // büyük harf → küçük harfle yazılır
        Assert.StartsWith(":root{--bg:#f3f6fa;", css);
        Assert.Contains("@media (prefers-color-scheme:dark){:root{--bg:#0a1220;", css);
        Assert.Contains("--brand:#c0392b;", css);
        foreach (var name in SitePaletteCss.SchemeTokenNames)
            Assert.Equal(2, Regex.Matches(css, Regex.Escape(name) + ":").Count);
        foreach (var name in SitePaletteCss.FixedTokenNames)
            Assert.Equal(1, Regex.Matches(css, Regex.Escape(name) + ":").Count);
        var declarations = CssDeclaration().Matches(css);
        Assert.Equal(2 * SitePaletteCss.SchemeTokenNames.Count + SitePaletteCss.FixedTokenNames.Count, declarations.Count);
        Assert.All(declarations, d => Assert.Matches("^#[0-9a-f]{6}$", d.Groups[2].Value));
    }

    [Fact]
    public void Sonda_NUL_ya_da_onaltilik_olmayan_karakter_reddedilir()
    {
        Assert.False(Rgb.TryParse("#12345\0", out _));
        Assert.False(Rgb.TryParse("#0b5d6\0", out _));
        Assert.False(Rgb.TryParse("#0b5d6 ", out _));
        Assert.False(Rgb.TryParse("#+b5d6b", out _));
        Assert.True(Rgb.TryParse("#0B5d6b", out var c));
        Assert.Equal(new Rgb(0x0b, 0x5d, 0x6b), c);
    }
}
