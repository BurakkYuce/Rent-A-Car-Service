using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç tip/model tanımı (master sözlük): araç modellerinin adlandırılmış listesi (ör. "Egea",
/// "Clio", "Corolla"). Tenant-owned + auditable. Marka serbest metin olarak ilişkilendirilebilir
/// (FK YOK). Araç formundaki Tip açılır listesini besler (additive — Vehicle.Tip string kalır).
/// </summary>
public class VehicleType : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    /// <summary>İlişkili marka (serbest metin, opsiyonel).</summary>
    public string? Marka { get; set; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
