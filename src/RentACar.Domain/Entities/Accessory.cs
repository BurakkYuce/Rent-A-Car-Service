using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Aksesuar/donanım tanımı (master sözlük): araç ek donanımlarının adlandırılmış listesi (ör.
/// "Bebek Koltuğu", "Kar Lastiği", "Navigasyon", "Kar Zinciri"). Tenant-owned + auditable. Araç/kira
/// formlarındaki Aksesuar açılır listesini besler (additive — serbest metin alan string kalır).
/// </summary>
public class Accessory : ITenantOwned, IAuditable
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
