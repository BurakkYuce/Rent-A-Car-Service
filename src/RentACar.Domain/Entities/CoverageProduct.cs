using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Sigorta & ek hizmet ürün kataloğu (canlı TürevRent sigorta_tarife_listesi karşılığı): online/
/// rezervasyonda satılan teminat (SCDW/CDW/IMM/LCF/PAI/Mini Hasar/Max Güvence…) ve paket/km hizmetlerinin
/// fiyat & kural master'ı. Her kalem: günlük birim ücret + KDV + faturalama gün tavanı (MaxGun) + döviz +
/// zorunluluk + TR/EN açıklama. Kira toplamına eklenen ek hizmet kalemlerinin master kaynağıdır; fiyat
/// motorunda (parite #7) ek-hizmet hesabının girdisidir. Tenant-owned + auditable. Saf fiyat-tanım —
/// deftere kayıt POSTLAMAZ. <see cref="Kod"/> tenant içinde benzersiz.
/// </summary>
public class CoverageProduct : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Ürün kodu (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    /// <summary>Ürün adı (TR).</summary>
    public string Ad { get; set; } = string.Empty;
    /// <summary>İngilizce ad (online/yabancı müşteri için; canlı TR+EN katalog).</summary>
    public string? AdEn { get; set; }
    public string? Aciklama { get; set; }

    public CoverageProductType Tur { get; set; } = CoverageProductType.Diger;

    /// <summary>Günlük birim ücret.</summary>
    public decimal? GunlukUcret { get; set; }
    /// <summary>KDV oranı (%).</summary>
    public decimal? KdvOrani { get; set; }
    /// <summary>Faturalanacak max gün tavanı (uzun kirada teminat ücreti N günle sınırlı; null = sınırsız).</summary>
    public int? MaxGun { get; set; }
    /// <summary>Para birimi (EURO/USD/TRY…). Boş → tenant varsayılanı.</summary>
    public string? Doviz { get; set; }
    /// <summary>Zorunlu teminat mı (ör. SCDW Zorunlu).</summary>
    public bool Zorunlu { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
