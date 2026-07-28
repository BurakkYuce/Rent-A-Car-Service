using RentACar.Application.Common;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-4.5: `StringComparer.OrdinalIgnoreCase`'in Türkçe `İ`/`I`/`ı` için sessizce yanlış davrandığı
/// (2. tur önerisinin kendisinin 3. turda çürütülmesi) — bu testler tam o durumu kanıtlar.
/// </summary>
public sealed class TurkishTextTests
{
    [Fact]
    public void Buyuk_i_noktali_kucuk_i_ile_eslesir()
    {
        // OrdinalIgnoreCase'in KAÇIRDIĞI durum: 'İ' (U+0130) ile 'i' ordinal-ignore-case'de eşleşmez.
        Assert.False(string.Equals("Dİ", "di", StringComparison.OrdinalIgnoreCase));
        Assert.True(TurkishText.EqualsIgnoreTurkishCase("Dİ", "di"));
    }

    [Fact]
    public void Canli_ornek_dizel_ekonomi_eslesir()
    {
        Assert.True(TurkishText.EqualsIgnoreTurkishCase("dizel", "DİZEL"));
        Assert.True(TurkishText.EqualsIgnoreTurkishCase("ekonomi", "Ekonomi"));
    }

    [Fact]
    public void Kucuk_turkce_harfler_de_eslenir()
    {
        Assert.Equal("sirket haberleri", TurkishText.Normalize("şirket haberleri"));
        Assert.True(TurkishText.EqualsIgnoreTurkishCase("Şirket", "sirket"));
    }

    [Fact]
    public void Farkli_metinler_eslesmez()
    {
        Assert.False(TurkishText.EqualsIgnoreTurkishCase("SUV", "Ekonomi"));
    }

    [Fact]
    public void Null_yalniz_null_ile_eslesir()
    {
        Assert.True(TurkishText.EqualsIgnoreTurkishCase(null, null));
        Assert.False(TurkishText.EqualsIgnoreTurkishCase(null, "x"));
        Assert.False(TurkishText.EqualsIgnoreTurkishCase("x", null));
    }

    [Fact]
    public void Gercek_veri_orneği_hicbir_gruba_eslesmez_ama_kendine_eslesir()
    {
        // Canlıda gözlenen serbest-metin sapması: case-fold ile çözülemez (bu bir case sorunu değil).
        Assert.False(TurkishText.EqualsIgnoreTurkishCase("FİAT-EGEA-MANUEL-DİZEL", "Ekonomi"));
        Assert.True(TurkishText.EqualsIgnoreTurkishCase("FİAT-EGEA-MANUEL-DİZEL", "fiat-egea-manuel-dizel"));
    }
}
