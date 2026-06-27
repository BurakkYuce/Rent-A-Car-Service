using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IRateCardRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class RateCardRepository(IDbContextFactory<AppDbContext> factory) : IRateCardRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<RateCard>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RateCards.AsNoTracking()
            .OrderBy(r => r.Grup).ThenBy(r => r.MinGun).ThenBy(r => r.Kod)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RateCard>> ListByGroupAsync(string grup, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Grup büyük/küçük harf duyarsız eşleşir; yalnız aktif tarifeler (lookup adayları).
        return await db.RateCards.AsNoTracking()
            .Where(r => r.Aktif && EF.Functions.ILike(r.Grup, grup))
            .ToListAsync(ct);
    }

    public async Task<RateCard?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RateCards.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.RateCards.AsNoTracking()
            .Where(r => r.Kod == k && (excludeId == null || r.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(RateCard rateCard, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.RateCards.Add(rateCard);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{rateCard.Kod}' kodlu tarife zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<RateCard> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rc = await db.RateCards.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rc is null) return false;

        apply(rc);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{rc.Kod}' kodlu tarife zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rc = await db.RateCards.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rc is null) return false;

        db.RateCards.Remove(rc);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
