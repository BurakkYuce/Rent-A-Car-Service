using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IVehicleRepository implementasyonu. Her işlemde factory'den kısa-ömürlü context
/// açar (Blazor Server'da uzun-ömürlü scoped DbContext eşzamanlılık hatasını önler).
/// Update, audit eski/yeni farkı için entity'yi yükleyip mutasyonu uygular.
/// </summary>
public sealed class VehicleRepository(IDbContextFactory<AppDbContext> factory) : IVehicleRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Vehicle>> ListAsync(string? sube = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Vehicles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sube)) q = q.Where(v => v.Sube == sube);
        return await q.OrderBy(v => v.Plaka).ToListAsync(ct);
    }

    public async Task<Vehicle?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<bool> PlakaExistsAsync(string plaka, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Vehicles
            .AsNoTracking()
            .Where(v => v.Plaka == plaka && (excludeId == null || v.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Vehicles.Add(vehicle);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicatePlakaException(vehicle.Plaka);
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Vehicle> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vehicle is null) return false;

        apply(vehicle); // mutasyon → ChangeTracker eski/yeni farkı yakalar (audit)
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicatePlakaException(vehicle.Plaka);
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vehicle is null) return false;

        db.Vehicles.Remove(vehicle);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
