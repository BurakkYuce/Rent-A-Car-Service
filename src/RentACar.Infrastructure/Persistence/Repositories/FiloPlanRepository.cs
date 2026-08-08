using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.FiloPlan;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Filo plan hedefi kalıcılığı (FAZ-19). Doğal-anahtar ihlali → ValidationException.</summary>
public sealed class FiloPlanRepository(IDbContextFactory<AppDbContext> factory) : IFiloPlanRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<FiloPlanHedefi>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FiloPlanHedefleri.AsNoTracking().ToListAsync(ct);
    }

    public async Task<FiloPlanHedefi?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FiloPlanHedefleri.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(FiloPlanHedefi row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.FiloPlanHedefleri.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException("Bu grup/SIPP/dönem için hedef zaten tanımlı."); }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<FiloPlanHedefi> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.FiloPlanHedefleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        apply(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException("Bu grup/SIPP/dönem için hedef zaten tanımlı."); }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.FiloPlanHedefleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        db.FiloPlanHedefleri.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
