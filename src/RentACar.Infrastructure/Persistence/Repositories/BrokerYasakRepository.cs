using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.BrokerYasaklari;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IBrokerYasakRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter ile
/// otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class BrokerYasakRepository(IDbContextFactory<AppDbContext> factory) : IBrokerYasakRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<BrokerYasak>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BrokerYasaklar.AsNoTracking().OrderBy(r => r.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BrokerYasak>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BrokerYasaklar.AsNoTracking().Where(r => r.Aktif).OrderBy(r => r.Ad).ToListAsync(ct);
    }

    public async Task<BrokerYasak?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BrokerYasaklar.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.BrokerYasaklar.AsNoTracking()
            .Where(r => r.Kod == k && (excludeId == null || r.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(BrokerYasak row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.BrokerYasaklar.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Kod}' kodlu broker yasağı zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<BrokerYasak> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.BrokerYasaklar.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        apply(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Kod}' kodlu broker yasağı zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.BrokerYasaklar.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        db.BrokerYasaklar.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
