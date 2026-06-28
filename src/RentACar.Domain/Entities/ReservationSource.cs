using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Rezervasyon kaynağı tanımı (master sözlük): rezervasyon/müşteri kaynaklarının adlandırılmış
/// listesi (ör. "Web", "Telefon", "Bayi", "Tavsiye"). Tenant-owned + auditable. Rezervasyon ve
/// cari formlarındaki Kaynak açılır listesini besler (additive).
/// </summary>
public class ReservationSource : ITenantOwned, IAuditable
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
