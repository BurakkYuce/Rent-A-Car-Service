using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// ICustomerRepository: kısa-ömürlü context'ler (factory). Update, audit eski/yeni
/// farkı için entity'yi yükleyip mutasyonu uygular. DB benzersizlik ihlali (23505),
/// constraint adına göre TC/VergiNo ayrımıyla DuplicateCariException'a çevrilir.
/// </summary>
public sealed class CustomerRepository(IDbContextFactory<AppDbContext> factory) : ICustomerRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking()
            .OrderBy(c => c.Tip).ThenBy(c => c.Unvan).ThenBy(c => c.Ad)
            .ToListAsync(ct);
    }

    public async Task<PagedResult<Customer>> SearchAsync(CustomerFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = $"%{filter.Query.Trim()}%";
            q = q.Where(c =>
                (c.Ad != null && EF.Functions.ILike(c.Ad, term))
                || (c.Soyad != null && EF.Functions.ILike(c.Soyad, term))
                || (c.Unvan != null && EF.Functions.ILike(c.Unvan, term))
                || (c.TcKimlik != null && EF.Functions.ILike(c.TcKimlik, term))
                || (c.VergiNo != null && EF.Functions.ILike(c.VergiNo, term)));
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderBy(c => c.Tip).ThenBy(c => c.Unvan).ThenBy(c => c.Ad)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(ct);
        return new PagedResult<Customer>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<Customer?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<bool> TcKimlikExistsAsync(string tcKimlik, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking()
            .Where(c => c.TcKimlik == tcKimlik && (excludeId == null || c.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task<bool> VergiNoExistsAsync(string vergiNo, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking()
            .Where(c => c.VergiNo == vergiNo && (excludeId == null || c.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Customer customer, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Customers.Add(customer);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (AsDuplicate(ex, customer) is { } dup)
        {
            throw dup;
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Customer> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return false;

        apply(customer);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (AsDuplicate(ex, customer) is { } dup)
        {
            throw dup;
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return false;

        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static DuplicateCariException? AsDuplicate(DbUpdateException ex, Customer c)
    {
        if (ex.InnerException is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
            return null;
        var constraint = pg.ConstraintName ?? string.Empty;
        if (constraint.Contains("TcKimlik", StringComparison.OrdinalIgnoreCase))
            return new DuplicateCariException("TC Kimlik No", c.TcKimlik ?? string.Empty);
        if (constraint.Contains("VergiNo", StringComparison.OrdinalIgnoreCase))
            return new DuplicateCariException("Vergi No", c.VergiNo ?? string.Empty);
        return new DuplicateCariException("kayıt", c.DisplayName);
    }
}
