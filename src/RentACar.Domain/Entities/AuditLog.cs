using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Denetim kaydı: kim / ne zaman / hangi entity / eski-yeni değerler.
/// Tenant-owned (RLS ile izole) ama IAuditable DEĞİL (kendini denetlemez).
/// Para hareketlerinde ve auditable entity değişikliklerinde zorunlu.
/// </summary>
public class AuditLog : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string EntityName { get; set; } = string.Empty;

    /// <summary>Etkilenen entity'nin PK'sı (string olarak; çoğunlukla Guid).</summary>
    public string EntityId { get; set; } = string.Empty;

    public AuditAction Action { get; set; }

    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Değişen alanların eski değerleri (jsonb). Create'te null.</summary>
    public string? OldValues { get; set; }

    /// <summary>Değişen alanların yeni değerleri (jsonb). Delete'te null.</summary>
    public string? NewValues { get; set; }
}
