using RentACar.Application.Common;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-15 — <see cref="TelefonLink"/>. Kural TEK yerde: aynı normalizasyon bugün
/// <c>rc-kira-tabs.js</c> içinde JS olarak da yaşıyor; yeni yüzeyler (site kabuğu, PR-17 talep
/// listesi) o JS'i klonlamak yerine bu sınıfı kullanıyor.
///
/// Bağımsız oracle: beklenen çıktılar elle yazılmış numaralardan türetiliyor, metodun kendi
/// mantığından değil. DB gerektirmediği için <c>[Collection("postgres")]</c> YOK.
/// </summary>
public sealed class TelefonLinkTests
{
    [Theory]
    // TR yerel yazımlar → 90'lı
    [InlineData("0532 123 45 67", "905321234567")]
    [InlineData("0090 532 123 45 67", "905321234567")]
    [InlineData("532 123 45 67", "905321234567")]
    [InlineData("+90 532 123 45 67", "905321234567")]
    [InlineData("(0532) 123-45-67", "905321234567")]
    // Sabit hat
    [InlineData("0242 123 45 67", "902421234567")]
    // Zaten ülke kodlu yabancı numara BOZULMAZ (yurt dışı müşteri)
    [InlineData("0049 151 12345678", "4915112345678")]
    public void Normalize_beklenen_rakam_dizisini_uretir(string girdi, string beklenen)
        => Assert.Equal(beklenen, TelefonLink.Normalize(girdi));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]                  // çok kısa
    [InlineData("bilinmiyor")]           // rakam yok
    [InlineData("1234567890123456789")]  // çok uzun
    public void Gecersiz_girdi_null_doner(string? girdi)
    {
        Assert.Null(TelefonLink.Normalize(girdi));
        Assert.Null(TelefonLink.Tel(girdi));
        Assert.Null(TelefonLink.Wa(girdi));
    }

    [Fact]
    public void Tel_ve_Wa_dogru_semayi_uretir()
    {
        Assert.Equal("tel:+905321234567", TelefonLink.Tel("0532 123 45 67"));
        Assert.Equal("https://wa.me/905321234567", TelefonLink.Wa("0532 123 45 67"));
    }

    [Fact]
    public void Wa_mesaji_URL_kodlar()
    {
        var url = TelefonLink.Wa("0532 123 45 67", "Merhaba, sözleşmeniz hazır & bekliyor");
        Assert.StartsWith("https://wa.me/905321234567?text=", url);
        Assert.DoesNotContain(" ", url);      // boşluk kodlandı
        Assert.DoesNotContain("&bekliyor", url);  // & kodlandı → query kırılmadı
        Assert.Contains("s%C3%B6zle%C5%9Fmeniz", url);  // Türkçe karakter UTF-8 kodlandı
    }
}
