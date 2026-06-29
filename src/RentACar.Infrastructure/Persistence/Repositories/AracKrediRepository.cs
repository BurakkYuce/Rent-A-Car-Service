using Microsoft.EntityFrameworkCore;
using RentACar.Application.AracKredileri;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Araç kredisi kalıcılığı (roadmap L4). CreateAsync boşluksuz No (KR-000001) tahsis eder.</summary>
public sealed class AracKrediRepository(IDbContextFactory<AppDbContext> factory) : IAracKrediRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<AracKredi>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AracKredileri.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<AracKredi?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AracKredileri.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(AracKredi row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); // No tahsisi atomik (boşluksuz)
        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "AracKrediNo", ct);
        row.No = $"KR-{n:D6}";
        db.AracKredileri.Add(row);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> TaksitOdeAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AracKredileri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        if (row.OdenenTaksit >= row.TaksitSayisi) return false; // tüm taksitler ödendi
        row.OdenenTaksit++;
        if (row.OdenenTaksit >= row.TaksitSayisi) row.Durum = KrediDurum.Kapandi;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetDurumAsync(Guid id, KrediDurum durum, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AracKredileri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        row.Durum = durum;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
