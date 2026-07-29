using Microsoft.EntityFrameworkCore;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>IVehiclePhotoRepository implementasyonu (PR-3). VehiclePhoto nav'sız child — her sorgu
/// VehicleId ile doğrudan (VehicleKmLog deseni).</summary>
public sealed class VehiclePhotoRepository(IDbContextFactory<AppDbContext> factory) : IVehiclePhotoRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<VehiclePhotoMeta>> ListMetaAsync(Guid vehicleId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehiclePhotos.AsNoTracking()
            .Where(p => p.VehicleId == vehicleId)
            .OrderBy(p => p.Sira).ThenBy(p => p.Id)
            .Select(p => new VehiclePhotoMeta(p.Id, p.Sira, p.ContentType))
            .ToListAsync(ct);
    }

    public async Task<VehiclePhoto?> FindAsync(Guid vehicleId, Guid photoId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehiclePhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.VehicleId == vehicleId && p.Id == photoId, ct);
    }

    public async Task<VehiclePhoto?> FindByIdAsync(Guid photoId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehiclePhotos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == photoId, ct);
    }

    public async Task<int> CountAsync(Guid vehicleId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehiclePhotos.CountAsync(p => p.VehicleId == vehicleId, ct);
    }

    public async Task<HashSet<Guid>> ListVehicleIdsWithPhotoAsync(
        IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct = default)
    {
        if (vehicleIds.Count == 0) return [];
        await using var db = await _factory.CreateDbContextAsync(ct);
        var idler = vehicleIds as Guid[] ?? [.. vehicleIds];
        // Npgsql `= ANY(@ids)`'e çevirir; Distinct sunucuda. Bytea kolonuna DOKUNULMAZ.
        var bulunan = await db.VehiclePhotos.AsNoTracking()
            .Where(p => idler.Contains(p.VehicleId))
            .Select(p => p.VehicleId)
            .Distinct()
            .ToListAsync(ct);
        return [.. bulunan];
    }

    public async Task AddAsync(VehiclePhoto photo, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var maxSira = await db.VehiclePhotos.Where(p => p.VehicleId == photo.VehicleId)
            .MaxAsync(p => (int?)p.Sira, ct) ?? -1;
        photo.Sira = maxSira + 1;
        db.VehiclePhotos.Add(photo);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid vehicleId, Guid photoId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var photo = await db.VehiclePhotos.FirstOrDefaultAsync(p => p.VehicleId == vehicleId && p.Id == photoId, ct);
        if (photo is null) return false;
        db.VehiclePhotos.Remove(photo);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task MoveAsync(Guid vehicleId, Guid photoId, int direction, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var current = await db.VehiclePhotos.FirstOrDefaultAsync(p => p.VehicleId == vehicleId && p.Id == photoId, ct);
        if (current is null) return;

        var q = db.VehiclePhotos.Where(p => p.VehicleId == vehicleId && p.Id != current.Id);
        var neighbor = direction < 0
            ? await q.Where(p => p.Sira < current.Sira).OrderByDescending(p => p.Sira).ThenByDescending(p => p.Id).FirstOrDefaultAsync(ct)
            : await q.Where(p => p.Sira > current.Sira).OrderBy(p => p.Sira).ThenBy(p => p.Id).FirstOrDefaultAsync(ct);
        if (neighbor is null) return; // sınırda — no-op

        (current.Sira, neighbor.Sira) = (neighbor.Sira, current.Sira);
        await db.SaveChangesAsync(ct);
    }
}
