using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.FuelKinds;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IFuelKindRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter
/// ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class FuelKindRepository(IDbContextFactory<AppDbContext> factory) : IFuelKindRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<FuelKind>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FuelKinds.AsNoTracking().OrderBy(k => k.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FuelKind>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FuelKinds.AsNoTracking().Where(k => k.Aktif).OrderBy(k => k.Ad).ToListAsync(ct);
    }

    public async Task<FuelKind?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FuelKinds.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.FuelKinds.AsNoTracking()
            .Where(x => x.Kod == k && (excludeId == null || x.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(FuelKind kind, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.FuelKinds.Add(kind);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{kind.Kod}' kodlu yakıt türü zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<FuelKind> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var kind = await db.FuelKinds.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (kind is null) return false;

        apply(kind);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{kind.Kod}' kodlu yakıt türü zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var kind = await db.FuelKinds.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (kind is null) return false;

        db.FuelKinds.Remove(kind);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
