using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Brands;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IBrandRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter
/// ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class BrandRepository(IDbContextFactory<AppDbContext> factory) : IBrandRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Brand>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Brands.AsNoTracking().OrderBy(b => b.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Brand>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Brands.AsNoTracking().Where(b => b.Aktif).OrderBy(b => b.Ad).ToListAsync(ct);
    }

    public async Task<Brand?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.Brands.AsNoTracking()
            .Where(b => b.Kod == k && (excludeId == null || b.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Brand brand, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Brands.Add(brand);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{brand.Kod}' kodlu marka zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Brand> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (brand is null) return false;

        apply(brand);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{brand.Kod}' kodlu marka zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (brand is null) return false;

        db.Brands.Remove(brand);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
