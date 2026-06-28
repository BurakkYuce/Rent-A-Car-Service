using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Sigorta şirketi tanımı (master sözlük): poliçe sağlayan sigorta şirketlerinin adlandırılmış
/// listesi (ör. "Allianz", "Axa", "Anadolu Sigorta"). Tenant-owned + auditable. Sigorta poliçe
/// formundaki Şirket açılır listesini besler (additive — serbest metin alan string kalır).
/// </summary>
public class InsuranceCompany : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Telefon { get; set; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
