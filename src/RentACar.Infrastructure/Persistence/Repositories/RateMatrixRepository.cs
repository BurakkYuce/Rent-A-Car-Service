using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.RateMatrices;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IRateMatrixRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter
/// ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class RateMatrixRepository(IDbContextFactory<AppDbContext> factory) : IRateMatrixRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<RateMatrix>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RateMatrices.AsNoTracking().OrderBy(r => r.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RateMatrix>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RateMatrices.AsNoTracking().Where(r => r.Aktif).OrderBy(r => r.Ad).ToListAsync(ct);
    }

    public async Task<RateMatrix?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RateMatrices.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.RateMatrices.AsNoTracking()
            .Where(r => r.Kod == k && (excludeId == null || r.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(RateMatrix row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.RateMatrices.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Kod}' kodlu tarife matrisi zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<RateMatrix> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.RateMatrices.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        apply(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Kod}' kodlu tarife matrisi zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.RateMatrices.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        db.RateMatrices.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
