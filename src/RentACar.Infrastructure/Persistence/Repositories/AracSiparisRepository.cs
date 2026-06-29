using Microsoft.EntityFrameworkCore;
using RentACar.Application.AracSiparisleri;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Araç sipariş kalıcılığı (roadmap L3). CreateAsync boşluksuz No (SP-000001) tahsis eder.</summary>
public sealed class AracSiparisRepository(IDbContextFactory<AppDbContext> factory) : IAracSiparisRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<AracSiparis>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AracSiparisleri.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<AracSiparis?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AracSiparisleri.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(AracSiparis row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); // No tahsisi atomik (boşluksuz)
        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "AracSiparisNo", ct);
        row.No = $"SP-{n:D6}";
        db.AracSiparisleri.Add(row);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> SetDurumAsync(Guid id, SiparisDurum durum, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AracSiparisleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        row.Durum = durum;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
