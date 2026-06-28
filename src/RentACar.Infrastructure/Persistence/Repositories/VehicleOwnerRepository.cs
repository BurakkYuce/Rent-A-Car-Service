using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.VehicleOwners;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IVehicleOwnerRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter
/// ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class VehicleOwnerRepository(IDbContextFactory<AppDbContext> factory) : IVehicleOwnerRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<VehicleOwner>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleOwners.AsNoTracking().OrderBy(o => o.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<VehicleOwner>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleOwners.AsNoTracking().Where(o => o.Aktif).OrderBy(o => o.Ad).ToListAsync(ct);
    }

    public async Task<VehicleOwner?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleOwners.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.VehicleOwners.AsNoTracking()
            .Where(o => o.Kod == k && (excludeId == null || o.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(VehicleOwner owner, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.VehicleOwners.Add(owner);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{owner.Kod}' kodlu araç sahibi zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<VehicleOwner> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var owner = await db.VehicleOwners.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (owner is null) return false;

        apply(owner);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{owner.Kod}' kodlu araç sahibi zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var owner = await db.VehicleOwners.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (owner is null) return false;

        db.VehicleOwners.Remove(owner);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
