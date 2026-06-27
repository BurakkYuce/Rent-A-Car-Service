using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// KDV oranı tanımı (master sözlük): tenant'ın kullandığı KDV oranlarının adlandırılmış listesi
/// (ör. "Genel %20" = 0.20, "İndirimli %10" = 0.10). Tenant-owned + auditable. Saf tanım tablosudur;
/// mevcut inline KDV alanlarını (fatura/gider/ek hizmet) DEĞİŞTİRMEZ — ileride açılır liste kaynağı.
/// Oran 0..1 konvansiyonu (0.20 = %20), EkHizmetTanim.KdvOrani ile tutarlı.
/// </summary>
public class KdvRate : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;

    /// <summary>KDV oranı (0..1, ör. 0.20 = %20).</summary>
    public decimal Oran { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
