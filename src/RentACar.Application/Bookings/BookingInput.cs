namespace RentACar.Application.Bookings;

/// <summary>Rezervasyon/Kira oluştur giriş modeli.</summary>
public sealed class BookingInput
{
    public Guid MusteriId { get; set; }
    public Guid? IkinciSurucuId { get; set; } // 2. sürücü (opsiyonel Customer bağı — sözleşmede gösterilir)

    // FAZ-47 — misafir (kayıtsız) 2. sürücü serbest metni. IkinciSurucuId ile BİRLİKTE dolu olamaz
    // (RentalService gürültülü reddeder). TC/ehliyet-no bilinçli YOK (şifreli PII — Domain notu).
    public string? IkinciSurucuSerbestAd { get; set; }
    public string? IkinciSurucuSerbestSoyad { get; set; }
    public string? IkinciSurucuSerbestTel { get; set; }
    public string? IkinciSurucuSerbestEhliyetSinifi { get; set; }
    /// <summary>FAZ-47 — ödeme şekli (bilgi; deftere/bakiyeye yansımaz).</summary>
    public string? OdemeSekli { get; set; }
    public Guid VehicleId { get; set; }
    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }
    public string? CikisOfisi { get; set; }
    public string? DonusOfisi { get; set; }
    public decimal GunlukUcret { get; set; }
    public int KmLimit { get; set; }
    public decimal FazlaKmUcret { get; set; }
    public decimal YakitBirimUcret { get; set; }

    // Ödeme-derinlik (roadmap A2; bilgi amaçlı, deftere yansımaz)
    public decimal? Provizyon { get; set; }
    public decimal? Depozito { get; set; }
    public decimal? KomisyonOran { get; set; }
    public decimal? KomisyonTutar { get; set; }
    public decimal? DropUcreti { get; set; }
    public decimal? SonraOdeOran { get; set; }

    public string? Aciklama { get; set; }
    public string? Kaynak { get; set; } // roadmap H2
    /// <summary>Promosyon kodu (FAZ 3.A5) — yalnız "Otomatik" fiyat türünde geçerli; aksi gürültülü red.</summary>
    public string? KampanyaKodu { get; set; }
    /// <summary>Dönemsel faturalama job kapısı (FAZ 4.2-B4; kira-başına opt-in).</summary>
    public bool DonemselFaturalama { get; set; }

    // TürevRent parite (additive metadata)
    public string? KiralamaTuru { get; set; }
    public string? FaturalamaTipi { get; set; }
    public string? FiyatTuru { get; set; }
    public string? Doviz { get; set; }

    // Kira formu detay alanları (mega-form; bilgi amaçlı — para hesabına girmez)
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

    /// <summary>Kira-seviyesi özel KDV oranı (kesir 0..1; FAZ 1.4) — fatura varsayılanı, Tutar'ı değiştirmez.</summary>
    public decimal? OzelKdvOran { get; set; }
    /// <summary>Kira-seviyesi damga vergisi (bilgi; FAZ 1.4) — fatura varsayılanı.</summary>
    public decimal? DamgaVergisi { get; set; }
    public string? TalepTuru { get; set; }
    public string? GeldigiBirim { get; set; }
    public string? KefilBilgisi { get; set; }
    public string? AssistFirma { get; set; }
    public string? OzelSoforBilgisi { get; set; }
    public string? EkKosullar { get; set; }
    /// <summary>Sözleşme PDF belge şablonu (BelgeSablon; bilgi/sunum — deftere yansımaz).</summary>
    public Guid? BelgeSablonId { get; set; }
    public int? ManuelFindexPuan { get; set; }
    /// <summary>Kira-seviyesi opsiyon (FAZ 4.4; bilgi).</summary>
    public decimal? OpsiyonNet { get; set; }
    public int? OpsiyonGun { get; set; }
    /// <summary>Risk limiti aşımı onayı (yalnız Yönetici/Admin işaretleyebilir — guard'da doğrulanır).</summary>
    public bool RiskOnay { get; set; }

    // FAZ 4.5 — OTA bileşen fiyatları (yalnız REZERVASYON yolu kullanır; kira create yok sayar).
    public decimal? OtaKiraBedeli { get; set; }
    public decimal? OtaDropBedeli { get; set; }
    public decimal? OtaBebekKoltugu { get; set; }
    public decimal? OtaNavigasyon { get; set; }
    public decimal? OtaLcf { get; set; }
    public decimal? OtaCdw { get; set; }
    public decimal? OtaScdw { get; set; }
    public decimal? OtaEkSurucu { get; set; }
    public bool? KabisCikis { get; set; }
    public bool? KabisDonus { get; set; }
    public bool? OtomatikUzat { get; set; }

    // Aksesuar tespiti (çıkış "önce"; dönüş alanları dönüş/güncelleme akışında)
    public bool? AksYedekAnahtarCikis { get; set; }
    public bool? AksStepneCikis { get; set; }
    public bool? AksZincirCikis { get; set; }
    public bool? AksIlkYardimCikis { get; set; }
    public string? AksLastikCikis { get; set; }
}
