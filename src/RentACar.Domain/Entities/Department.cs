using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Departman tanımı (master sözlük): organizasyon birimlerinin adlandırılmış listesi (ör. "Operasyon",
/// "Muhasebe", "Filo", "Satış"). Tenant-owned + auditable. Gider/personel dağıtımı ve raporlama için
/// sözlük (additive — ilgili serbest metin alanlar string kalır).
/// </summary>
public class Department : ITenantOwned, IAuditable
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
