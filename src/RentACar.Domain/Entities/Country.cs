using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Ülke tanımı (master sözlük): ülkelerin adlandırılmış listesi (ör. "Türkiye", "Almanya", "ABD").
/// Tenant-owned + auditable. Cari/sürücü uyruğu ve adres formlarındaki Ülke açılır listesini besler
/// (additive — serbest metin alan string kalır).
/// </summary>
public class Country : ITenantOwned, IAuditable
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
