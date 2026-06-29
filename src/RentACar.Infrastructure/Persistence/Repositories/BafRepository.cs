using Microsoft.EntityFrameworkCore;
using RentACar.Application.Baflar;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>BAF kalıcılığı (roadmap L5). CreateAsync boşluksuz No (BAF-000001) tahsis eder.</summary>
public sealed class BafRepository(IDbContextFactory<AppDbContext> factory) : IBafRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Baf>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Baflar.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<Baf?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Baflar.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(Baf row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); // No tahsisi atomik (boşluksuz)
        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "BafNo", ct);
        row.No = $"BAF-{n:D6}";
        db.Baflar.Add(row);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> TeslimAlAsync(Guid id, int donusKm, int? donusYakit, DateTimeOffset donusTarihi, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Baflar.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        row.DonusTarihi = donusTarihi;
        row.DonusKm = donusKm;
        row.DonusYakit = donusYakit;
        row.Durum = BafDurum.Kapandi;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> IptalAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Baflar.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        row.Durum = BafDurum.Iptal;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
