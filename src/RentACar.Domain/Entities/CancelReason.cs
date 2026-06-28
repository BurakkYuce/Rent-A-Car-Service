using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// İptal sebebi tanımı (master sözlük): rezervasyon/kira iptallerinde seçilen sebeplerin
/// adlandırılmış listesi (ör. "Müşteri Vazgeçti", "Araç Arızası"). Tenant-owned + auditable.
/// İptal akışlarında açılır liste kaynağı (additive — mevcut iptal mantığını değiştirmez).
/// </summary>
public class CancelReason : ITenantOwned, IAuditable
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
