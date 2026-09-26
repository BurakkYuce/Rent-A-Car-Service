using RentACar.Domain.Enums;

namespace RentACar.Application.RateMatrices;

/// <summary>Tarife matrisi oluştur/güncelle giriş modeli. Fiyat/kapsam alanları opsiyonel.</summary>
public sealed class RateMatrixInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    public string? Kanal { get; set; }
    public string? Sube { get; set; }
    public string? Lokasyon { get; set; }
    public string? AracGrupKod { get; set; }
    public string? ParaBirimi { get; set; }

    public DateTimeOffset? BasTar { get; set; }
    public DateTimeOffset? BitTar { get; set; }

    public decimal? Gun1 { get; set; }
    public decimal? Gun2 { get; set; }
    public decimal? Gun3 { get; set; }
    public decimal? Gun4 { get; set; }
    public decimal? Gun5 { get; set; }
    public decimal? Gun6 { get; set; }
    public decimal? Gun7 { get; set; }

    /// <summary>Uzun-dönem kademeleri (FAZ 3.A1): 8-29 gün haftalık / 30+ gün aylık günlük fiyat.</summary>
    public decimal? GunHaftalik { get; set; }
    public decimal? GunAylik { get; set; }

    // FAZ-71 — kademe bazlı km limiti (GÜNLÜK, gün sayısına çarpılır) + aşım ücreti.
    public int? Km1 { get; set; }
    public int? Km2 { get; set; }
    public int? Km3 { get; set; }
    public int? Km4 { get; set; }
    public int? Km5 { get; set; }
    public int? Km6 { get; set; }
    public decimal? Km1Ucret { get; set; }
    public decimal? Km2Ucret { get; set; }
    public decimal? Km3Ucret { get; set; }
    public decimal? Km4Ucret { get; set; }
    public decimal? Km5Ucret { get; set; }
    public decimal? Km6Ucret { get; set; }
    public int? KmHaftalik { get; set; }
    public decimal? KmHaftalikUcret { get; set; }
    public int? KmAylik { get; set; }
    public decimal? KmAylikUcret { get; set; }

    public decimal? MaxEsneklik { get; set; }

    public TariffApprovalStatus OnayDurumu { get; set; } = TariffApprovalStatus.Bekliyor;
    public string? Onaylayan { get; set; }
    public DateTimeOffset? OnayZaman { get; set; }

    public bool Aktif { get; set; } = true;

    // ---- FAZ-70 ----
    /// <summary>Satır etiketi ("Fiyat"/"Kampanya") — motor davranışını değiştirmez.</summary>
    public string? Turu { get; set; }
    /// <summary>Max Kira Kapsamı (gün); aşan kiralarda satır adaylıktan elenir. null = sınırsız.</summary>
    public int? KiraSuresi { get; set; }
}
