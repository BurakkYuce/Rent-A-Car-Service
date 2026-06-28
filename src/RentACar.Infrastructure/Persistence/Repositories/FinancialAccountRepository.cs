using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.FinancialAccounts;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IFinancialAccountRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class FinancialAccountRepository(IDbContextFactory<AppDbContext> factory) : IFinancialAccountRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<FinancialAccount>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FinancialAccounts.AsNoTracking().OrderBy(a => a.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FinancialAccount>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FinancialAccounts.AsNoTracking().Where(a => a.Aktif).OrderBy(a => a.Ad).ToListAsync(ct);
    }

    public async Task<FinancialAccount?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FinancialAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.FinancialAccounts.AsNoTracking()
            .Where(a => a.Kod == k && (excludeId == null || a.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(FinancialAccount account, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.FinancialAccounts.Add(account);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{account.Kod}' kodlu hesap zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<FinancialAccount> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var account = await db.FinancialAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null) return false;

        apply(account);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{account.Kod}' kodlu hesap zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var account = await db.FinancialAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null) return false;

        db.FinancialAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
