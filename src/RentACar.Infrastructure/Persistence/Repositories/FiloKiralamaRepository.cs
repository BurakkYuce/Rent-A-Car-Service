using Microsoft.EntityFrameworkCore;
using RentACar.Application.FiloKiralamalar;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Filo kiralama kalıcılığı (roadmap L1). CreateAsync boşluksuz No (FK-000001) tahsis eder.</summary>
public sealed class FiloKiralamaRepository(IDbContextFactory<AppDbContext> factory) : IFiloKiralamaRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<FiloKiralama>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FiloKiralamalar.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<FiloKiralama?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FiloKiralamalar.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(FiloKiralama row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); // No tahsisi atomik (boşluksuz)
        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "FiloKiralamaNo", ct);
        row.No = $"FK-{n:D6}";
        db.FiloKiralamalar.Add(row);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> SetDurumAsync(Guid id, FiloKiraDurum durum, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.FiloKiralamalar.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        row.Durum = durum;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
