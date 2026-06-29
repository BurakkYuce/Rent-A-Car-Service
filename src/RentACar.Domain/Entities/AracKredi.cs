using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç kredisi takibi (roadmap L4): banka kredisi + taksit planı + kalan bakiye. Banka ENTEGRASYONU YOK
/// (salt kayıt/hesap). Tenant-owned + auditable; full-CRUD. DEFTER POSTLAMAZ — ödeme defteri ileride
/// (kredi taksiti gideri) ayrı; bu kayıt kredi takibinin kaynağıdır.
/// </summary>
public class AracKredi : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (KR-000001).</summary>
    public string No { get; set; } = string.Empty;

    public string BankaAdi { get; set; } = string.Empty;
    public Guid? VehicleId { get; set; }

    public decimal KrediTutari { get; set; }
    public decimal FaizOran { get; set; }   // yıllık basit faiz (kesir)
    public int TaksitSayisi { get; set; }
    public DateTimeOffset BaslangicTarihi { get; set; } = DateTimeOffset.UtcNow;
    public int OdenenTaksit { get; set; }   // ödenen taksit adedi (kalan bakiye için)

    public string Currency { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;

    public KrediDurum Durum { get; set; } = KrediDurum.Aktif;
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
