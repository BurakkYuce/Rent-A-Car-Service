using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Renk tanımı (master sözlük): araç renklerinin adlandırılmış listesi (ör. "Beyaz", "Siyah",
/// "Gri", "Kırmızı"). Tenant-owned + auditable. Araç formundaki Renk açılır listesini besler.
/// </summary>
public class VehicleColor : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
