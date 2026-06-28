using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Accessories;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IAccessoryRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter ile
/// otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class AccessoryRepository(IDbContextFactory<AppDbContext> factory) : IAccessoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Accessory>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Accessories.AsNoTracking().OrderBy(a => a.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Accessory>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Accessories.AsNoTracking().Where(a => a.Aktif).OrderBy(a => a.Ad).ToListAsync(ct);
    }

    public async Task<Accessory?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Accessories.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.Accessories.AsNoTracking()
            .Where(a => a.Kod == k && (excludeId == null || a.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Accessory accessory, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Accessories.Add(accessory);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{accessory.Kod}' kodlu aksesuar zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Accessory> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var accessory = await db.Accessories.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (accessory is null) return false;

        apply(accessory);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{accessory.Kod}' kodlu aksesuar zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var accessory = await db.Accessories.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (accessory is null) return false;

        db.Accessories.Remove(accessory);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
