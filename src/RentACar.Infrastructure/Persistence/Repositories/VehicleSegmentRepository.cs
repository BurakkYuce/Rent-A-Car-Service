using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.VehicleSegments;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IVehicleSegmentRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class VehicleSegmentRepository(IDbContextFactory<AppDbContext> factory) : IVehicleSegmentRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<VehicleSegment>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleSegments.AsNoTracking().OrderBy(s => s.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<VehicleSegment>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleSegments.AsNoTracking().Where(s => s.Aktif).OrderBy(s => s.Ad).ToListAsync(ct);
    }

    public async Task<VehicleSegment?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleSegments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.VehicleSegments.AsNoTracking()
            .Where(s => s.Kod == k && (excludeId == null || s.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(VehicleSegment segment, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.VehicleSegments.Add(segment);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{segment.Kod}' kodlu segment zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<VehicleSegment> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var segment = await db.VehicleSegments.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (segment is null) return false;

        apply(segment);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{segment.Kod}' kodlu segment zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var segment = await db.VehicleSegments.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (segment is null) return false;

        db.VehicleSegments.Remove(segment);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
