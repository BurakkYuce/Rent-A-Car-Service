using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.ExpenseCategories;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IExpenseCategoryRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class ExpenseCategoryRepository(IDbContextFactory<AppDbContext> factory) : IExpenseCategoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<ExpenseCategory>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ExpenseCategories.AsNoTracking().OrderBy(c => c.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ExpenseCategory>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ExpenseCategories.AsNoTracking().Where(c => c.Aktif).OrderBy(c => c.Ad).ToListAsync(ct);
    }

    public async Task<ExpenseCategory?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ExpenseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = code.Trim().ToUpperInvariant();
        return await db.ExpenseCategories.AsNoTracking()
            .Where(c => c.Kod == k && (excludeId == null || c.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(ExpenseCategory category, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.ExpenseCategories.Add(category);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{category.Kod}' kodlu gider türü zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<ExpenseCategory> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return false;

        apply(category);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{category.Kod}' kodlu gider türü zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return false;

        db.ExpenseCategories.Remove(category);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // F11.1a — IVersionedRepository<ExpenseCategory> (generic RowVersion helper).
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => RowVersion.ReadAsync<ExpenseCategory>(_factory, id, ct);

    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default) => RowVersion.ReadAllAsync<ExpenseCategory>(_factory, ct);

    public Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<ExpenseCategory> apply, CancellationToken ct = default)
        => RowVersion.UpdateAsync(_factory, id, expectedVersion, apply, c => $"'{c.Kod}' kodlu gider türü zaten var.", ct);
}
