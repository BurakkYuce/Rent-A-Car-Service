using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Marka tanımı (master sözlük): araç markalarının adlandırılmış listesi (Fiat, Renault…).
/// Tenant-owned + auditable. Araç formundaki serbest-metin Marka alanını açılır listeden besler;
/// Vehicle.Marka kolonu STRING kalır (FK YOK — additive).
/// </summary>
public class Brand : ITenantOwned, IAuditable
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
