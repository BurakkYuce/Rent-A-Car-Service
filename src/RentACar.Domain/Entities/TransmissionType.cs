using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Vites türü tanımı (master sözlük): araç şanzıman tiplerinin adlandırılmış listesi (ör. "Manuel",
/// "Otomatik", "Yarı Otomatik", "CVT"). Tenant-owned + auditable. Tanım/raporlama sözlüğü.
/// </summary>
public class TransmissionType : ITenantOwned, IAuditable
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
