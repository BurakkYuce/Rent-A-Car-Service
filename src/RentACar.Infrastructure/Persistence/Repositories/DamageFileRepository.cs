using Microsoft.EntityFrameworkCore;
using RentACar.Application.DamageFiles;
using RentACar.Domain.Entities;

using RentACar.Domain.Common;

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
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            file.No = await BelgeNoUretici.UretAsync(db, db.TenantId, DocumentNoType.HasarDosyasi, ct);
            db.DamageFiles.Add(file);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (PkIhlali.Mi(ex)) // F6.1b: Id = işlem anahtarı → çift gönderim
            {
                await tx.RollbackAsync(ct);
                throw new RentACar.Application.Common.DuplicateOperationException(PkIhlali.Mesaj);
            }
            await tx.CommitAsync(ct);
        }, ct);
    }

    public Task<bool> UpdateLockedAsync(Guid id, Action<DamageFile> apply, CancellationToken ct = default)
        => SatirSurumu.GuncelleAsync(_factory, SatirSurumu.HasarDosyalari, id, beklenenSurum: null,
            (db, k, c) => db.DamageFiles.FirstOrDefaultAsync(f => f.Id == k, c), apply, ct);

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
