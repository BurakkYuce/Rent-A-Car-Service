namespace RentACar.Application.Bookings;

/// <summary>
/// Açık kira güncelleme giriş modeli (mega-form "Kaydet"). WHITELIST TİP DÜZEYİNDE ZORLANIR:
/// para/tarih/durum alanları (BasTar/BitTar/Gun/GunlukUcret/Tutar/GenelToplam/Tahsilat/Bakiye/Durum/
/// FiyatTuru/Doviz/KurSnapshot/MusteriId/VehicleId) bu tipte YOKTUR — form ne gönderirse göndersin
/// değişemezler. Tarih değişikliği yalnız ExtendAsync; fiyat farkları fark faturası akışıyla.
/// Form tam-durum gönderir (tüm alanlar prefill) → null = alan temizlendi (null yazılır);
/// KmLimit/FazlaKmUcret/YakitBirimUcret'te null → 0 (create semantiğiyle aynı: 0 = sınırsız/ücretsiz).
/// </summary>
public sealed class RentalUpdateInput
{
    public string? CikisOfisi { get; set; }
    public string? DonusOfisi { get; set; }
    public Guid? IkinciSurucuId { get; set; }
    public string? Aciklama { get; set; }
    public string? Kaynak { get; set; }
    public string? KiralamaTuru { get; set; }
    public string? FaturalamaTipi { get; set; }

    // Aşım parametreleri (yalnız Kirada değiştirilebilir — dönüşte ReturnMath bunlarla hesaplar)
    public int KmLimit { get; set; }
    public decimal FazlaKmUcret { get; set; }
    public decimal YakitBirimUcret { get; set; }

    // Ödeme-derinlik (bilgi amaçlı; deftere yansımaz)
    public decimal? Provizyon { get; set; }
    public decimal? Depozito { get; set; }
    public decimal? KomisyonOran { get; set; }
    public decimal? KomisyonTutar { get; set; }
    public decimal? DropUcreti { get; set; }
    public decimal? SonraOdeOran { get; set; }

    // Detay alanları (bilgi amaçlı)
    public string? UyariAciklama { get; set; }
    public string? OzelFaturaAciklama { get; set; }
    public bool? FaturaListesindeGizle { get; set; }
    public string? UcusNo { get; set; }
    public string? ProvizyonNo { get; set; }
    public DateTimeOffset? ProvizyonTarih { get; set; }
    public string? OnayKodu { get; set; }
    public string? FirmaKodu { get; set; }
    public string? ProjeAdi { get; set; }
    public string? OzelKod { get; set; }

    /// <summary>FAZ 1.4 — bilgi/fatura-varsayılanı (Tutar'ı değiştirmez; Tamamlandi'da da güncellenebilir).</summary>
    public decimal? OzelKdvOran { get; set; }
    public decimal? DamgaVergisi { get; set; }
    public string? TalepTuru { get; set; }
    public string? GeldigiBirim { get; set; }
    public string? KefilBilgisi { get; set; }
    public string? AssistFirma { get; set; }
    public string? OzelSoforBilgisi { get; set; }
    public string? EkKosullar { get; set; }
    public int? ManuelFindexPuan { get; set; }
    public bool? KabisCikis { get; set; }
    public bool? KabisDonus { get; set; }
    public bool? OtomatikUzat { get; set; }

    // Aksesuar tespiti (çıkış + dönüş)
    public bool? AksYedekAnahtarCikis { get; set; }
    public bool? AksYedekAnahtarDonus { get; set; }
    public bool? AksStepneCikis { get; set; }
    public bool? AksStepneDonus { get; set; }
    public bool? AksZincirCikis { get; set; }
    public bool? AksZincirDonus { get; set; }
    public bool? AksIlkYardimCikis { get; set; }
    public bool? AksIlkYardimDonus { get; set; }
    public string? AksLastikCikis { get; set; }
    public string? AksLastikDonus { get; set; }
}

/// <summary>Dönüş canlı önizleme sonucu (GET /kiralar/donus-hesapla → JSON). Ok=false → nazik hata.</summary>
public sealed record KiraDonusOnizleme(
    bool Ok, string? Hata,
    int KullanilanKm = 0, int FazlaKm = 0, decimal FazlaKmBedeli = 0m,
    int EksikYakit = 0, decimal YakitBedeli = 0m,
    int UzatmaGun = 0, decimal UzatmaBedeli = 0m,
    decimal EkHizmetToplam = 0m, decimal YeniGenelToplam = 0m, decimal Kalan = 0m)
{
    public static KiraDonusOnizleme Hatali(string mesaj) => new(false, mesaj);
}
