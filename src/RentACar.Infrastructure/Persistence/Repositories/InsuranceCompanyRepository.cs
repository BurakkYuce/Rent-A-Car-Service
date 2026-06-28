using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.InsuranceCompanies;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IInsuranceCompanyRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class InsuranceCompanyRepository(IDbContextFactory<AppDbContext> factory) : IInsuranceCompanyRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<InsuranceCompany>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InsuranceCompanies.AsNoTracking().OrderBy(c => c.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<InsuranceCompany>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InsuranceCompanies.AsNoTracking().Where(c => c.Aktif).OrderBy(c => c.Ad).ToListAsync(ct);
    }

    public async Task<InsuranceCompany?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InsuranceCompanies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.InsuranceCompanies.AsNoTracking()
            .Where(c => c.Kod == k && (excludeId == null || c.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(InsuranceCompany company, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.InsuranceCompanies.Add(company);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{company.Kod}' kodlu sigorta şirketi zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<InsuranceCompany> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var company = await db.InsuranceCompanies.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (company is null) return false;

        apply(company);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{company.Kod}' kodlu sigorta şirketi zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var company = await db.InsuranceCompanies.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (company is null) return false;

        db.InsuranceCompanies.Remove(company);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
