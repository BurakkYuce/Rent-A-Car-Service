using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.ReservationSources;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IReservationSourceRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class ReservationSourceRepository(IDbContextFactory<AppDbContext> factory) : IReservationSourceRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<ReservationSource>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ReservationSources.AsNoTracking().OrderBy(s => s.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReservationSource>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ReservationSources.AsNoTracking().Where(s => s.Aktif).OrderBy(s => s.Ad).ToListAsync(ct);
    }

    public async Task<ReservationSource?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ReservationSources.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.ReservationSources.AsNoTracking()
            .Where(s => s.Kod == k && (excludeId == null || s.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(ReservationSource source, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.ReservationSources.Add(source);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{source.Kod}' kodlu rezervasyon kaynağı zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<ReservationSource> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var source = await db.ReservationSources.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return false;

        apply(source);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{source.Kod}' kodlu rezervasyon kaynağı zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var source = await db.ReservationSources.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return false;

        db.ReservationSources.Remove(source);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
