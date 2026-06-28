using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Müşteri memnuniyet anketi (roadmap C3; CRM). Tenant-owned kayıt; opsiyonel cari ilişkisi. Puan 0-10.
/// </summary>
public class Anket : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid? CariId { get; set; }
    public int Puan { get; set; }
    public string? Yorum { get; set; }
    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;
    public string? Kaynak { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
