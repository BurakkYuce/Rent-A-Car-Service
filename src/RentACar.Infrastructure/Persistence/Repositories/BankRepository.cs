using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Banks;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IBankRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter ile
/// otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class BankRepository(IDbContextFactory<AppDbContext> factory) : IBankRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Bank>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Banks.AsNoTracking().OrderBy(b => b.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Bank>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Banks.AsNoTracking().Where(b => b.Aktif).OrderBy(b => b.Ad).ToListAsync(ct);
    }

    public async Task<Bank?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Banks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.Banks.AsNoTracking()
            .Where(b => b.Kod == k && (excludeId == null || b.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Bank bank, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Banks.Add(bank);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{bank.Kod}' kodlu banka zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Bank> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var bank = await db.Banks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bank is null) return false;

        apply(bank);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{bank.Kod}' kodlu banka zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var bank = await db.Banks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bank is null) return false;

        db.Banks.Remove(bank);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
