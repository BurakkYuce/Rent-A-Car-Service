using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.KdvRates;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IKdvRateRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class KdvRateRepository(IDbContextFactory<AppDbContext> factory) : IVatRateRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<KdvRate>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KdvOranlari.AsNoTracking().OrderBy(r => r.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KdvRate>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KdvOranlari.AsNoTracking().Where(r => r.Aktif).OrderBy(r => r.Oran).ToListAsync(ct);
    }

    public async Task<KdvRate?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KdvOranlari.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> CodeExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.KdvOranlari.AsNoTracking()
            .Where(r => r.Kod == k && (excludeId == null || r.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(KdvRate rate, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.KdvOranlari.Add(rate);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{rate.Kod}' kodlu KDV oranı zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<KdvRate> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rate = await db.KdvOranlari.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rate is null) return false;

        apply(rate);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{rate.Kod}' kodlu KDV oranı zaten var.");
        }
        return true;
    }

    /// <summary>F11.1b — satır kilidi + iyimser sürüm karşılaştırması (<see cref="SatirSurumu"/>).</summary>
    public async Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<KdvRate> apply, CancellationToken ct = default)
    {
        string? code = null;
        try
        {
            return await SatirSurumu.GuncelleAsync(_factory, SatirSurumu.KdvRates, id, expectedVersion,
                (db, k, c) => db.KdvOranlari.FirstOrDefaultAsync(x => x.Id == k, c),
                x => { apply(x); code = x.Kod; }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{code}' kodlu KDV oranı zaten var.");
        }
    }

    public async Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SatirSurumu.OkuAsync(db, SatirSurumu.KdvRates, id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rate = await db.KdvOranlari.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rate is null) return false;

        db.KdvOranlari.Remove(rate);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
