using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Kira ek hizmet kalemi: bir kira sözleşmesine eklenen fiyatlı opsiyon (bebek koltuğu, GPS,
/// ek sürücü…). EkHizmetTanim'dan türetilir ama tutar/oran SNAPSHOT'lanır (tanım sonradan
/// değişse bile sözleşme tutarı sabit kalır). Birim ücret NET'tir (EkHizmetTanim gibi);
/// NetTutar/KdvTutar/Toplam servis tarafından hesaplanıp saklanır (KdvMath.FromNet).
/// Tenant-owned + auditable; kira faturalanmadan önce eklenip çıkarılabilir.
/// </summary>
public class RentalAddOn : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid RentalId { get; set; }
    /// <summary>Kaynak ek hizmet tanımı (snapshot alındıktan sonra yalnız referans).</summary>
    public Guid EkHizmetTanimId { get; set; }

    /// <summary>Tanımdan snapshot'lanan ad (fatura/kayıt için).</summary>
    public string Ad { get; set; } = string.Empty;

    public decimal Miktar { get; set; } = 1m;
    /// <summary>Birim NET fiyat (snapshot; tanım veya serbest override).</summary>
    public decimal BirimNetFiyat { get; set; }
    /// <summary>KDV oranı (0..1) snapshot.</summary>
    public decimal KdvOrani { get; set; }

    // Hesaplanmış (servis doldurur; KdvMath.FromNet ile kuruş tutarlı)
    public decimal NetTutar { get; set; }
    public decimal KdvTutar { get; set; }
    /// <summary>Brüt = NetTutar + KdvTutar.</summary>
    public decimal Toplam { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
