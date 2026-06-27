using Microsoft.EntityFrameworkCore;
using RentACar.Application.Auditing;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Salt-okunur denetim kaydı sorgusu (en yeni önce). Filtre: entity adı / kullanıcı (ILIKE) +
/// aksiyon. Tenant izolasyonu query filter + RLS ile otomatik.
/// </summary>
public sealed class AuditRepository(IDbContextFactory<AppDbContext> factory) : IAuditRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<PagedResult<AuditLog>> SearchAsync(AuditFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.EntityName))
            q = q.Where(a => EF.Functions.ILike(a.EntityName, $"%{filter.EntityName.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(filter.UserName))
            q = q.Where(a => a.UserName != null && EF.Functions.ILike(a.UserName, $"%{filter.UserName.Trim()}%"));
        if (filter.Action is { } act)
            q = q.Where(a => a.Action == act);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(a => a.TimestampUtc)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(ct);
        return new PagedResult<AuditLog>(items, total, filter.Page, filter.PageSize);
    }
}
