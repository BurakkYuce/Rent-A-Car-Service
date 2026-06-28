using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Dönem kilidi (roadmap D2; canlı "dönem kapanışı" karşılığı). Tenant başına TEK satır (TenantId unique).
/// <see cref="KapanisTarihi"/> ve ÖNCESİ kapalı: o tarihe/öncesine defter kaydı (tahsilat/ödeme/virman/
/// fatura/gider/ceza/hgs) postlanamaz. null = kilit yok (tüm dönemler açık).
/// </summary>
public class DonemKilidi : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public DateTimeOffset? KapanisTarihi { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
