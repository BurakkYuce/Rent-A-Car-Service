using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Departments;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IDepartmentRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter
/// ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class DepartmentRepository(IDbContextFactory<AppDbContext> factory) : IDepartmentRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Department>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Departments.AsNoTracking().OrderBy(d => d.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Department>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Departments.AsNoTracking().Where(d => d.Aktif).OrderBy(d => d.Ad).ToListAsync(ct);
    }

    public async Task<Department?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.Departments.AsNoTracking()
            .Where(d => d.Kod == k && (excludeId == null || d.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Department department, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Departments.Add(department);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{department.Kod}' kodlu departman zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Department> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null) return false;

        apply(department);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{department.Kod}' kodlu departman zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null) return false;

        db.Departments.Remove(department);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
