using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Müşteri şikayeti (roadmap C3; CRM). Tenant-owned kayıt; opsiyonel cari ilişkisi. Durum takipli (çözüm notu).
/// </summary>
public class Sikayet : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid? CariId { get; set; }
    public string Konu { get; set; } = string.Empty;
    public string? Detay { get; set; }
    public SikayetDurum Durum { get; set; } = SikayetDurum.Acik;
    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;
    public string? Cozum { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
