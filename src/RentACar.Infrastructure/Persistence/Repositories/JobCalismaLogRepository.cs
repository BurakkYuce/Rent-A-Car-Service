using Microsoft.EntityFrameworkCore;
using RentACar.Application.Jobs;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IJobCalismaLogRepository — salt okuma (yazımı <c>JobCalismaKaydedici</c> ham SQL ile yapar).
/// Tenant izolasyonu RLS + merkezi query filter ile otomatik.
/// </summary>
public sealed class JobCalismaLogRepository(IDbContextFactory<AppDbContext> factory) : IJobCalismaLogRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<JobCalismaLog>> ListAsync(
        JobCalismaLogFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.JobCalismaLoglari.AsNoTracking().AsQueryable();

        if (filter is not null)
        {
            if (!string.IsNullOrWhiteSpace(filter.JobAdi))
            {
                var j = filter.JobAdi.Trim();
                q = q.Where(r => r.JobAdi == j);
            }
            if (filter.Bas is { } b) q = q.Where(r => r.BaslangicUtc >= b);
            if (filter.Bit is { } t) q = q.Where(r => r.BaslangicUtc <= t);
            if (filter.YalnizHatali is true) q = q.Where(r => !r.Basarili);
        }

        // Günlük hızla büyür → en yeni önce + üst sınır (sayfa yükü sınırlı kalsın).
        var limit = Math.Clamp(filter?.EnFazla ?? 500, 1, 5000);
        return await q.OrderByDescending(r => r.BaslangicUtc).Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<JobCalismaLog>> SonKosularAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // İş adı başına EN YENİ satır. Az sayıda iş adı var → gruplu alt-sorgu yeterli.
        return await db.JobCalismaLoglari.AsNoTracking()
            .GroupBy(r => r.JobAdi)
            .Select(g => g.OrderByDescending(r => r.BaslangicUtc).First())
            .ToListAsync(ct);
    }
}
