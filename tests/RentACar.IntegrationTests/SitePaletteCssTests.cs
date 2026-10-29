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
        Assert.True(Contrast(t.DayInk.Hex, t.DayBg.Hex) >= 4.5, $"{theme}/{palette}: day-ink/day-bg");
        Assert.True(Contrast(t.NightInk.Hex, t.NightBg.Hex) >= 4.5, $"{theme}/{palette}: night-ink/night-bg");
        // Koyu şema daima koyu. Açık şema açık — set KOYU KİMLİK bildirmediyse (DarkInLightMode): bayrak yanlışlıkla
        // koyu yazılmış bir açık şemayı yakalar, bilinçli olanı belgeler.
        Assert.True(Lum(t.Dark.Bg.Hex) < Lum(t.Dark.Ink.Hex), $"{theme}/{palette}: koyu şemanın zemini mürekkepten koyu");
        if (t.Palette.DarkInLightMode)
            Assert.True(Lum(t.Light.Bg.Hex) < Lum(t.Light.Ink.Hex), $"{theme}/{palette}: koyu kimlikli setin açık şeması koyu");
        else
        {
            Assert.True(Lum(t.Light.Bg.Hex) > Lum(t.Light.Ink.Hex), $"{theme}/{palette}: açık şemanın zemini mürekkepten açık");
            // Şemadan bağımsız sabitler: koyu levha (gündüz mürekkebi) üstünde gece mürekkebi okunur (kontuar panosu).
            // Koyu kimlikli sette gündüz mürekkebi açık renktir — "koyu levha" tanımı orada yok, kullanılmaz.
            Assert.True(Contrast(t.NightInk.Hex, t.DayInk.Hex) >= 4.5, $"{theme}/{palette}: night-ink/day-ink");
        }

        // Palet bloğu her şemanın color-scheme'ini ZEMİNDEN yazar (form denetimleri, light-dark() seçimi). Beklenen
        // değer bağımsız: zeminin göreli parlaklığı mürekkebinkinden küçükse koyu.
        var css = SitePaletteCss.Css(t);
        var lightBlock = css[..css.IndexOf("@media", StringComparison.Ordinal)];
        var darkBlock = css[css.IndexOf("@media", StringComparison.Ordinal)..];
        // ";color-scheme:" — medya sorgusundaki "prefers-color-scheme:dark" ile karışmasın.
        Assert.Contains($";color-scheme:{(Lum(t.Light.Bg.Hex) < Lum(t.Light.Ink.Hex) ? "dark" : "light")}", lightBlock);
        Assert.Contains(";color-scheme:dark", darkBlock);
    }

    /// <summary>Koyu kimlikli set (açık şema da koyu; bugün kayıtlı temalarda yok, üretici desteği korunur): ana renk
    /// dolgusunun üstündeki koyu yazı adayı açık şemanın ZEMİNİDİR (mürekkep açık renk). Elle hesap: platin #d6cfc2
    /// L≈0.62; grafit #1c1f23 L≈0.013 → (0.67)/(0.063) ≈ 10.7:1 ≥ beyazın 1.55:1'i → iki şemada da dolgu üstü yazı
    /// #1c1f23. Saf siyaha (#000000) düşülmez.</summary>
    [Fact]
    public void Koyu_kimlikli_sette_dolgu_ustu_yazi_acik_semanin_zemini()
    {
        var dark = new SitePalette("koyu", "Koyu",
            new SchemeColors("#1c1f23", "#24282d", "#f1efea", "#d6cfc2"),
            new SchemeColors("#121417", "#1a1d21", "#ecebe6", "#d6cfc2")) { DarkInLightMode = true };
        var grafit = SitePaletteCss.Tokens(dark, null);
        Assert.True(grafit.Palette.DarkInLightMode);
        Assert.Equal(("#d6cfc2", "#1c1f23"), (grafit.Light.Brand.Hex, grafit.Light.OnBrand.Hex));
        Assert.Equal(("#d6cfc2", "#1c1f23"), (grafit.Dark.Brand.Hex, grafit.Dark.OnBrand.Hex));
        Assert.True(Contrast("#1c1f23", "#d6cfc2") >= 4.5);
        var css = SitePaletteCss.Css(grafit);
        Assert.Equal(2, Regex.Matches(css, ";color-scheme:dark").Count);
        Assert.DoesNotContain(";color-scheme:light", css);

        // Açık kimlikli set (Anahtar varsayılanı): açık şema açık, bildirimi light.
        var light = SitePaletteCss.Css(SitePaletteCss.Tokens(SiteThemes.Anahtar, null, null));
        Assert.StartsWith(":root{--bg:#fbf8f3;", light);
        Assert.Contains("color-scheme:light}", light);
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

    /// <summary>Varsayılan setler: her temanın varsayılan setinin ELLE YAZILMIŞ temel renkleri (zemin, kart, mürekkep,
    /// ana renk, vurgu; Anahtar'da ikincil metin) AA'yı zaten tuttuğu için AYNEN basılır. Ana renk üstü yazı elle:
    /// #0f6e5a / #1d4ed8 / #8c2f1b üstünde beyaz 6.2 / 6.7 / 8.3:1 → #ffffff. Vurgu üstü yazı: kehribar/amber
    /// üstünde mürekkep (≥ 8:1). Paket'in vurgusu ana rengi izler.</summary>
    [Fact]
    public void Varsayilan_setler_bugunku_renkleri_korur()
    {
        var anahtar = SitePaletteCss.Tokens(SiteThemes.Anahtar, null, null);
        Assert.Equal("zumrut", anahtar.Palette.Key);
        Assert.Equal(("#fbf8f3", "#ffffff", "#1e2329", "#5b6470", "#0f6e5a", "#ffffff", "#f2b33d", "#1e2329"),
            (anahtar.Light.Bg.Hex, anahtar.Light.Surface.Hex, anahtar.Light.Ink.Hex, anahtar.Light.InkMuted.Hex,
                anahtar.Light.Brand.Hex, anahtar.Light.OnBrand.Hex, anahtar.Light.Accent.Hex, anahtar.Light.AccentInk.Hex));
        Assert.Equal(("#111614", "#19201d", "#e9eeeb", "#4fbf9f"),
            (anahtar.Dark.Bg.Hex, anahtar.Dark.Surface.Hex, anahtar.Dark.Ink.Hex, anahtar.Dark.Brand.Hex));
        Assert.Equal(("#fbf8f3", "#1e2329", "#111614", "#e9eeeb"),
            (anahtar.DayBg.Hex, anahtar.DayInk.Hex, anahtar.NightBg.Hex, anahtar.NightInk.Hex));

        var kiyas = SitePaletteCss.Tokens(SiteThemes.Kiyas, null, null);
        Assert.Equal("mavi", kiyas.Palette.Key);
        Assert.Equal(("#f4f6f8", "#ffffff", "#14171c", "#566070", "#1d4ed8", "#ffffff", "#fbbf24", "#14171c"),
            (kiyas.Light.Bg.Hex, kiyas.Light.Surface.Hex, kiyas.Light.Ink.Hex, kiyas.Light.InkMuted.Hex,
                kiyas.Light.Brand.Hex, kiyas.Light.OnBrand.Hex, kiyas.Light.Accent.Hex, kiyas.Light.AccentInk.Hex));

        var paket = SitePaletteCss.Tokens(SiteThemes.Paket, null, null);
        Assert.Equal("kiremit", paket.Palette.Key);
        Assert.Equal(("#f3f2ee", "#ffffff", "#111316", "#5e5f63", "#8c2f1b", "#ffffff"),
            (paket.Light.Bg.Hex, paket.Light.Surface.Hex, paket.Light.Ink.Hex, paket.Light.InkMuted.Hex,
                paket.Light.Brand.Hex, paket.Light.OnBrand.Hex));
        Assert.Equal(paket.Light.Brand, paket.Light.Accent); // tek vurgu: vurgu ana rengi izler
    }

    [Fact]
    public void Sari_ana_renk_acikta_dolguda_ve_metinde_koyulastirilir_koyuda_aynen_kalir()
    {
        var t = SitePaletteCss.Tokens(SiteThemes.Anahtar, null, "#ffd400");
        // Sarı açık zeminle ~1.3:1 — düğme sayfada kaybolurdu (WCAG 1.4.11) → dolgu koyulaştırıldı.
        Assert.NotEqual("#ffd400", t.Light.Brand.Hex);
        Assert.True(Contrast(t.Light.Brand.Hex, "#fbf8f3") >= 3.0);
        Assert.Equal("#1e2329", t.Light.OnBrand.Hex); // temanın mürekkebi koyulaşmış sarı üstünde
        Assert.True(Contrast("#1e2329", t.Light.Brand.Hex) >= 4.5);
        Assert.NotEqual("#ffd400", t.Light.BrandText.Hex);
        Assert.True(Contrast(t.Light.BrandText.Hex, "#fbf8f3") >= 4.5);
        // Sarı koyu zeminde zaten okunuyor → koyu şemada dolgu ve metin aynen kalır.
        Assert.Equal("#ffd400", t.Dark.Brand.Hex);
        Assert.Equal("#ffd400", t.Dark.BrandText.Hex);
    }

    [Fact]
    public void Lacivert_ana_renk_acikta_aynen_koyuda_acilir()
    {
        var t = SitePaletteCss.Tokens(SiteThemes.Anahtar, null, "#0b1f3a");
        Assert.Equal("#0b1f3a", t.Light.Brand.Hex);
        Assert.Equal("#0b1f3a", t.Light.BrandText.Hex);
        Assert.Equal("#ffffff", t.Light.OnBrand.Hex);
        Assert.NotEqual("#0b1f3a", t.Dark.Brand.Hex); // koyu zeminde düğme kaybolurdu
        Assert.True(Contrast(t.Dark.Brand.Hex, "#111614") >= 3.0);
        Assert.True(Contrast(t.Dark.Brand.Hex, "#19201d") >= 3.0);
    }

    /// <summary>Orta gri (#808080): beyaz 3.95:1, Anahtar mürekkebi (#1e2329) ~4.0:1 — ikisi de AA'nın altında. Elle
    /// hesap: gri L=0.216 → saf siyah (0.216+0.05)/0.05 = 5.32:1 → siyah seçilmeli.</summary>
    [Fact]
    public void Orta_tonda_ne_beyaz_ne_murekkep_yeterse_saf_siyah_secilir()
    {
        Assert.Equal("#000000", SitePaletteCss.Tokens(SiteThemes.Anahtar, null, "#808080").Light.OnBrand.Hex);
        Assert.True(Contrast("#1e2329", "#808080") < 4.5); // senaryonun kendisi: mürekkep yetmiyor
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
    [InlineData(null, "zumrut")]
    [InlineData("", "zumrut")]
    [InlineData("olmayan", "zumrut")]
    [InlineData("mavi", "zumrut")]                  // kıyas'ın seti → anahtar'da varsayılan
    [InlineData("domates", "zumrut")]               // sökülen pazar'ın seti → varsayılan
    [InlineData("zumrut;}</style><script>", "zumrut")]
    [InlineData(" Visne ", "visne")]
    [InlineData("grafit", "grafit")]
    public void Palet_anahtari_temanin_setlerinde_aranir_yoksa_varsayilan(string? key, string expected)
    {
        Assert.Equal(expected, SitePaletteCss.Resolve(SiteThemes.Anahtar, key).Key);
        var css = SitePaletteCss.Build(SiteThemes.Anahtar, key, null);
        Assert.Equal(SitePaletteCss.Build(SiteThemes.Anahtar, expected, null), css);
        Assert.DoesNotContain("<", css);
    }

    [GeneratedRegex(@"(--[a-z-]+):([^;}]*)")]
    private static partial Regex CssDeclaration();

    [Fact]
    public void Css_iki_semayi_tum_tokenlarla_ve_yalniz_hex_degerle_yazar()
    {
        var css = SitePaletteCss.Build(SiteThemes.Kiyas, "turkuaz", "#C0392B"); // büyük harf → küçük harfle yazılır
        Assert.StartsWith(":root{--bg:#f2f6f6;", css);
        Assert.Contains("@media (prefers-color-scheme:dark){:root{--bg:#0b1414;", css);
        Assert.Contains("--brand:#c0392b;", css);
        foreach (var name in SitePaletteCss.SchemeTokenNames)
            Assert.Equal(2, Regex.Matches(css, Regex.Escape(name) + ":").Count);
        foreach (var name in SitePaletteCss.FixedTokenNames)
            Assert.Equal(1, Regex.Matches(css, Regex.Escape(name) + ":").Count);
        var declarations = CssDeclaration().Matches(css);
        Assert.Equal(2 * SitePaletteCss.SchemeTokenNames.Count + SitePaletteCss.FixedTokenNames.Count, declarations.Count);
        Assert.All(declarations, d => Assert.Matches("^#[0-9a-f]{6}$", d.Groups[2].Value));
        // Token dışındaki TEK bildirim: şemanın color-scheme'i (sabit anahtar sözcük, girdi değil).
        Assert.Equal(["light", "dark"], Regex.Matches(css, ";color-scheme:([a-z]+)").Select(m => m.Groups[1].Value));
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
