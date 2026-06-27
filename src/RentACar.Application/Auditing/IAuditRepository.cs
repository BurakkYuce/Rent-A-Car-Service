using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Auditing;

/// <summary>Salt-okunur denetim kaydı sorgusu. Tenant izolasyonu query filter + RLS ile otomatik.</summary>
public interface IAuditRepository
{
    Task<PagedResult<AuditLog>> SearchAsync(AuditFilter filter, CancellationToken ct = default);
}
