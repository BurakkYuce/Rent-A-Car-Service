using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç segment tanımı (master sözlük): araçların pazarlama/fiyat segmentleri (ör. "Ekonomik",
/// "Orta", "Üst", "Lüks", "SUV"). Tenant-owned + auditable. Araç ve araç grubu formlarındaki
/// Segment açılır listesini besler (additive — Vehicle.Segment string kalır, FK YOK).
/// </summary>
public class VehicleSegment : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
