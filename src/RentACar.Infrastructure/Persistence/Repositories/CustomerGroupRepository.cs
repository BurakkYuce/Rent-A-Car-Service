using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.CustomerGroups;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// ICustomerGroupRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter
/// ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class CustomerGroupRepository(IDbContextFactory<AppDbContext> factory) : ICustomerGroupRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<CustomerGroup>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.CustomerGroups.AsNoTracking().OrderBy(g => g.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CustomerGroup>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.CustomerGroups.AsNoTracking().Where(g => g.Aktif).OrderBy(g => g.Ad).ToListAsync(ct);
    }

    public async Task<CustomerGroup?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.CustomerGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.CustomerGroups.AsNoTracking()
            .Where(g => g.Kod == k && (excludeId == null || g.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(CustomerGroup group, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.CustomerGroups.Add(group);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{group.Kod}' kodlu müşteri grubu zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<CustomerGroup> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var group = await db.CustomerGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return false;

        apply(group);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{group.Kod}' kodlu müşteri grubu zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var group = await db.CustomerGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return false;

        db.CustomerGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
