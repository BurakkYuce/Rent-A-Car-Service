using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>Araç MTV (motorlu taşıtlar vergisi) dönem kaydı. Vade panosunu besler.</summary>
public class MtvRecord : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid VehicleId { get; set; }

    /// <summary>Dönem, ör. "2026-1" / "2026-2".</summary>
    public string Donem { get; set; } = string.Empty;
    public decimal Tutar { get; set; }
    public DateTimeOffset Vade { get; set; }
    public bool Odendi { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
