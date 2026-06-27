using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
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

    public async Task<PagedResult<Vehicle>> SearchAsync(VehicleFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Vehicles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Sube)) q = q.Where(v => v.Sube == filter.Sube);
        if (filter.Durum is { } d) q = q.Where(v => v.Durum == d);
        if (!string.IsNullOrWhiteSpace(filter.Grup)) q = q.Where(v => v.Grup == filter.Grup);
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = $"%{filter.Query.Trim()}%";
            q = q.Where(v => EF.Functions.ILike(v.Plaka, term)
                || (v.Marka != null && EF.Functions.ILike(v.Marka, term)));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(v => v.Plaka)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(ct);
        return new PagedResult<Vehicle>(items, total, filter.Page, filter.PageSize);
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
