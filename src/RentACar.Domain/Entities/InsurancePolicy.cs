using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç sigorta poliçesi (Trafik/Kasko). Tenant-owned + auditable. Güncellenebilir kayıt
/// (mali belge değil → değişmezlik trigger'ı yok). Bitiş tarihi vade panosunu besler.
/// </summary>
public class InsurancePolicy : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid VehicleId { get; set; }
    public InsuranceType Tip { get; set; } = InsuranceType.Trafik;

    public string? PoliceNo { get; set; }
    public DateTimeOffset Baslangic { get; set; }
    public DateTimeOffset Bitis { get; set; }
    public string? Firma { get; set; }
    public string? Acenta { get; set; }

    public decimal Prim { get; set; }
    public string Currency { get; set; } = "TRY";

    /// <summary>Zeyil/ek prim (roadmap J3): ödemede prime eklenir.</summary>
    public decimal ZeyilPrim { get; set; }
    /// <summary>Ödendi mi (roadmap J3): true → defter kaydı yazılmış, tekrar ödenemez.</summary>
    public bool Odendi { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
