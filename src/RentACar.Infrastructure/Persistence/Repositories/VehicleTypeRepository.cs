using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.VehicleTypes;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IVehicleTypeRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter
/// ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class VehicleTypeRepository(IDbContextFactory<AppDbContext> factory) : IVehicleTypeRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<VehicleType>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleTypes.AsNoTracking().OrderBy(t => t.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<VehicleType>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleTypes.AsNoTracking().Where(t => t.Aktif).OrderBy(t => t.Ad).ToListAsync(ct);
    }

    public async Task<VehicleType?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<bool> CodeExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.VehicleTypes.AsNoTracking()
            .Where(t => t.Kod == k && (excludeId == null || t.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(VehicleType type, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.VehicleTypes.Add(type);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{type.Kod}' kodlu araç tipi zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<VehicleType> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var type = await db.VehicleTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return false;

        apply(type);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{type.Kod}' kodlu araç tipi zaten var.");
        }
        return true;
    }

    /// <summary>F6.1a — satır kilidi + iyimser sürüm karşılaştırması (<see cref="SatirSurumu"/>).</summary>
    public async Task<bool> UpdateAsync(Guid id, string? beklenenSurum, Action<VehicleType> apply, CancellationToken ct = default)
    {
        string? kod = null;
        try
        {
            return await SatirSurumu.GuncelleAsync(_factory, SatirSurumu.AracTipleri, id, beklenenSurum,
                (db, k, c) => db.VehicleTypes.FirstOrDefaultAsync(t => t.Id == k, c),
                t => { apply(t); kod = t.Kod; }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{kod}' kodlu araç tipi zaten var.");
        }
    }

    public async Task<string?> VersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SatirSurumu.OkuAsync(db, SatirSurumu.AracTipleri, id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var type = await db.VehicleTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return false;

        db.VehicleTypes.Remove(type);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
