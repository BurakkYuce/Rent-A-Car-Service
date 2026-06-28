using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Müşteri grubu tanımı (master sözlük): carilerin sınıflandırma gruplarının adlandırılmış listesi
/// (ör. "Bireysel", "Kurumsal", "Acente", "Filo"). Tenant-owned + auditable. Cari formundaki Grup
/// açılır listesini besler (additive — serbest metin alan string kalır).
/// </summary>
public class CustomerGroup : ITenantOwned, IAuditable
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
