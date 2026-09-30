using RentACar.PublicSite;

namespace RentACar.IntegrationTests;

/// <summary>
/// Halka açık site marka paleti — saf birim testi (DB yok).
///
/// <para>BAĞIMSIZ ORACLE: beklenen kontrast değerleri koddan değil yayımlanmış WCAG referanslarından
/// gelir (siyah/beyaz = 21:1; #767676 beyaz üstünde 4.54:1 — AA sınırındaki bilinen gri). Uyarlanmış
/// renklerin "geçip geçmediği" ise palet kodunu çağırmayan AYRI bir kontrast hesabıyla
/// (<see cref="OracleContrast"/>) doğrulanır.</para>
/// </summary>
public sealed class BrandPaletteTests
{
    // ---- Bağımsız WCAG kontrast hesabı (üretim kodunu çağırmaz) ----
    private static double OracleLum(string hex)
    {
        static double Ch(string h)
        {
            var c = Convert.ToInt32(h, 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(hex.Substring(1, 2)) + 0.7152 * Ch(hex.Substring(3, 2)) + 0.0722 * Ch(hex.Substring(5, 2));
    }

    private static double OracleContrast(string a, string b)
    {
        var (x, y) = (OracleLum(a), OracleLum(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    [Fact]
    public void Kontrast_formulu_yayimlanmis_referanslarla_ayni()
    {
        Assert.Equal(21.0, BrandPalette.Contrast(new(0, 0, 0), new(255, 255, 255)), 3);
        // WCAG örneklerinde AA sınırı olarak geçen gri: beyaz üstünde 4.54:1.
        Assert.Equal(4.54, BrandPalette.Contrast(new(0x76, 0x76, 0x76), new(255, 255, 255)), 2);
        Assert.Equal(1.0, BrandPalette.Contrast(new(10, 20, 30), new(10, 20, 30)), 6);
    }

    [Fact]
    public void Sari_firma_rengi_acikta_dolguda_ve_metinde_koyulastirilir_koyuda_aynen_kalir()
    {
        var p = BrandPalette.For("#ffd400");

        // Sarı açık kağıtla ~1.4:1 — düğme sayfada kaybolurdu (WCAG 1.4.11) → dolgu koyulaştırıldı.
        Assert.NotEqual("#ffd400", p.Light.Fill);
        Assert.True(OracleContrast(p.Light.Fill, BrandPalette.LightPaper) >= 3.0);
        Assert.True(OracleContrast(p.Light.OnFill, p.Light.Fill) >= 4.5);
        Assert.NotEqual("#ffd400", p.Light.Text);         // metin varyantı koyulaştırıldı
        Assert.True(OracleContrast(p.Light.Text, BrandPalette.LightPaper) >= 4.5);
        Assert.True(OracleContrast(p.Light.Text, "#ffffff") >= 4.5);
        // Sarı zaten koyu zeminde okunuyor → koyu temada dolgu ve metin aynen kalır.
        Assert.Equal("#ffd400", p.Dark.Fill);
        Assert.Equal("#ffd400", p.Dark.Text);
    }

    [Fact]
    public void Lacivert_firma_rengi_acikta_aynen_koyuda_acilir()
    {
        var p = BrandPalette.For("#0b1f3a");

        Assert.Equal("#0b1f3a", p.Light.Fill);
        Assert.Equal("#0b1f3a", p.Light.Text);            // açık zeminde zaten 4.5:1 üstü
        Assert.Equal("#ffffff", p.Light.OnFill);
        Assert.NotEqual("#0b1f3a", p.Dark.Fill);          // koyu zeminde düğme kaybolurdu
        Assert.True(OracleContrast(p.Dark.Fill, BrandPalette.DarkPaper) >= 3.0);
        Assert.True(OracleContrast(p.Dark.Text, BrandPalette.DarkPaper) >= 4.5);
        Assert.True(OracleContrast(p.Dark.Text, BrandPalette.DarkSurface) >= 4.5);
    }

    /// <summary>Orta gri (#808080): beyaz 3.95:1, sitenin mürekkebi (#1d2227) 4.06:1 — ikisi de AA'nın
    /// altında. Elle hesap: gri L=0.216 → saf siyah (0.216+0.05)/0.05 = 5.32:1 → siyah seçilmeli.</summary>
    [Fact]
    public void Orta_tonda_ne_beyaz_ne_murekkep_yeterse_saf_siyah_secilir()
    {
        Assert.Equal("#000000", BrandPalette.For("#808080").Light.OnFill);
        Assert.True(OracleContrast("#000000", "#808080") >= 4.5);
        Assert.True(OracleContrast("#1d2227", "#808080") < 4.5); // senaryonun kendisi: mürekkep yetmiyor
    }

    /// <summary>6×6×6 renk ızgarası (216 renk; siyah, beyaz, doygun uçlar dahil): her renkte AA
    /// değişmezleri tutmalı. Tek örnekle yakalanmayan sınır durumları (orta griler, açık cyan) burada.</summary>
    [Fact]
    public void Her_renkte_AA_degismezleri_tutar()
    {
        string[] steps = ["00", "33", "66", "99", "cc", "ff"];
        foreach (var r in steps)
        foreach (var g in steps)
        foreach (var b in steps)
        {
            var hex = $"#{r}{g}{b}";
            var p = BrandPalette.For(hex);

            // Kağıtla zaten 3:1 veren firma rengi AYNEN korunur; vermeyen koyulaştırılır.
            if (OracleContrast(hex, BrandPalette.LightPaper) >= 3.0) Assert.Equal(hex, p.Light.Fill);
            Assert.True(OracleContrast(p.Light.Fill, BrandPalette.LightPaper) >= 3.0, $"{hex} açık dolgu");
            Assert.True(OracleContrast(p.Light.OnFill, p.Light.Fill) >= 4.5, $"{hex} açık dolgu yazısı");
            Assert.True(OracleContrast(p.Light.Text, BrandPalette.LightPaper) >= 4.5, $"{hex} açık metin");
            Assert.True(OracleContrast(p.Light.Text, p.Light.Soft) >= 4.5, $"{hex} açık metin / hafif ton");
            Assert.True(OracleContrast(p.Dark.OnFill, p.Dark.Fill) >= 4.5, $"{hex} koyu dolgu yazısı");
            Assert.True(OracleContrast(p.Dark.Fill, BrandPalette.DarkPaper) >= 3.0, $"{hex} koyu dolgu");
            Assert.True(OracleContrast(p.Dark.Fill, BrandPalette.DarkSurface) >= 3.0, $"{hex} koyu kart dolgu");
            Assert.True(OracleContrast(p.Dark.Text, BrandPalette.DarkPaper) >= 4.5, $"{hex} koyu metin");
            Assert.True(OracleContrast(p.Dark.Text, BrandPalette.DarkSurface) >= 4.5, $"{hex} koyu kart metni");
            Assert.True(OracleContrast(p.Dark.Text, p.Dark.Soft) >= 4.5, $"{hex} koyu metin / hafif ton");
        }
    }

    /// <summary>İncelemede görünmez bulunan dolgular: kağıt #f5f6f3 üstünde sarı ~1.07, açık yeşil ~1.3,
    /// beyaz ~1.08:1. Her biri kağıtla 3:1'e koyulaştırılmalı, üstündeki yazı yine AA.</summary>
    [Theory]
    [InlineData("#ffff00")]
    [InlineData("#00ff00")]
    [InlineData("#ffffff")]
    public void Kagitta_gorunmeyen_dolgu_koyulastirilir(string hex)
    {
        Assert.True(OracleContrast(hex, BrandPalette.LightPaper) < 3.0); // senaryonun kendisi
        var p = BrandPalette.For(hex);
        Assert.NotEqual(hex, p.Light.Fill);
        Assert.True(OracleContrast(p.Light.Fill, BrandPalette.LightPaper) >= 3.0);
        Assert.True(OracleContrast(p.Light.OnFill, p.Light.Fill) >= 4.5);
    }

    /// <summary>İncelemede #808080 için metin/hafif ton 4.15 (açık) ve 3.95 (koyu), #ffff00 için 4.46 ölçüldü.</summary>
    [Theory]
    [InlineData("#808080")]
    [InlineData("#ffff00")]
    public void Marka_metni_hafif_ton_uzerinde_de_AA(string hex)
    {
        var p = BrandPalette.For(hex);
        Assert.True(OracleContrast(p.Light.Text, p.Light.Soft) >= 4.5);
        Assert.True(OracleContrast(p.Dark.Text, p.Dark.Soft) >= 4.5);
    }

    [Fact]
    public void Sonda_NUL_ya_da_onaltilik_olmayan_karakter_reddedilir()
    {
        Assert.False(BrandPalette.Rgb.TryParse("#12345\0", out _));
        Assert.False(BrandPalette.Rgb.TryParse("#0b5d6\0", out _));
        Assert.False(BrandPalette.Rgb.TryParse("#0b5d6 ", out _));
        Assert.False(BrandPalette.Rgb.TryParse("#+b5d6b", out _));
        Assert.True(BrandPalette.Rgb.TryParse("#0B5d6b", out var c));
        Assert.Equal(new BrandPalette.Rgb(0x0b, 0x5d, 0x6b), c);
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
    public void Bicimsiz_deger_varsayilana_duser_ve_CSSe_sizmaz(string? value)
    {
        Assert.Equal(BrandPalette.For(BrandPalette.DefaultColor), BrandPalette.For(value));
        var css = BrandPalette.Css(value);
        Assert.DoesNotContain("<", css);
        Assert.DoesNotContain("script", css);
        Assert.Contains($"--brand:{BrandPalette.DefaultColor}", css);
    }

    [Fact]
    public void Css_iki_temayi_ve_dort_degiskeni_yazar()
    {
        var css = BrandPalette.Css("#C0392B"); // büyük harf → çözümlenir, küçük harfle yazılır
        Assert.StartsWith(":root{--brand:#c0392b;--on-brand:#ffffff;--brand-text:", css);
        Assert.Contains("@media (prefers-color-scheme:dark){:root{--brand:", css);
        foreach (var name in new[] { "--brand:", "--on-brand:", "--brand-text:", "--brand-soft:" })
            Assert.Equal(2, css.Split(name).Length - 1);
    }
}
