using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Locations;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// ILocationRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class LocationRepository(IDbContextFactory<AppDbContext> factory) : ILocationRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Location>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Locations.AsNoTracking().OrderBy(l => l.Kod).ToListAsync(ct);
    }

    public async Task<Location?> FindByAdAsync(string ad, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // F4.1 adversarial N1: eşleşme OfficeBranchInterceptor ile AYNI anahtarla ve BELLEKTE (SQL lower()
        // Türkçe 'İ'yi .NET'ten farklı küçültüyordu). Aynı adda Kod sırası deterministik (C4 deseniyle aynı).
        var anahtar = OfisAdiAnahtari.Uret(ad);
        return (await db.Locations.AsNoTracking().ToListAsync(ct))
            .Where(l => OfisAdiAnahtari.Uret(l.Ad) == anahtar)
            .OrderBy(l => l.Kod) // interceptor ile aynı sıralama
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<Location>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Locations.AsNoTracking().Where(l => l.Aktif).OrderBy(l => l.Ad).ToListAsync(ct);
    }

    public async Task<Location?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.Locations.AsNoTracking()
            .Where(l => l.Kod == k && (excludeId == null || l.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Location location, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Locations.Add(location);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{location.Kod}' kodlu ofis zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Location> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var loc = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (loc is null) return false;

        apply(loc);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{loc.Kod}' kodlu ofis zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var loc = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (loc is null) return false;

        db.Locations.Remove(loc);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
