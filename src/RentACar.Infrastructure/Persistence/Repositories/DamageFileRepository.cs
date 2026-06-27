using Microsoft.EntityFrameworkCore;
using RentACar.Application.DamageFiles;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Hasar dosyası (BAF) kalıcılığı. Boşluksuz No + onay akışı güncellemeleri.</summary>
public sealed class DamageFileRepository(IDbContextFactory<AppDbContext> factory) : IDamageFileRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<DamageFile>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DamageFiles.AsNoTracking().OrderByDescending(f => f.AcilisTarihi).ToListAsync(ct);
    }

    public async Task<DamageFile?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DamageFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
    }

    public async Task CreateAsync(DamageFile file, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "DamageFileNo", ct);
        file.No = $"BAF-{n:D6}";
        db.DamageFiles.Add(file);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<DamageFile> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var file = await db.DamageFiles.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null) return false;
        apply(file);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
