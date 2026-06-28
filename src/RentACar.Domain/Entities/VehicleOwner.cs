using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç sahip(lik) grubu tanımı (master sözlük): aracın mülkiyet grubu (ör. "Bizim", "Dış",
/// "Operasyonel Kiralama", "Filo Kiralama"). Tenant-owned + auditable. Araç sahip filtrelerini
/// (Bizim/Dış) ve raporları besler (additive — serbest metin sahip alanı kalır, FK YOK).
/// </summary>
public class VehicleOwner : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    /// <summary>Sahiplik türü (serbest metin, ör. "Bizim", "Dış"). Opsiyonel.</summary>
    public string? Tur { get; set; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
