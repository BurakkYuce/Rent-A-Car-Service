using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IYetkiGrupRepository (PR-D): ekran-izni şablonu deposu. Tenant izolasyonu RLS + query filter.
/// Ad benzersiz (TenantId, Ad); upsert ada göre. 23505 → ValidationException.
/// </summary>
public sealed class YetkiGrupRepository(IDbContextFactory<AppDbContext> factory) : IPermissionGroupRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<YetkiGrup>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.YetkiGruplari.AsNoTracking().OrderBy(r => r.Ad).ToListAsync(ct);
    }

    public async Task<YetkiGrup?> FindByNameAsync(string ad, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.YetkiGruplari.AsNoTracking().FirstOrDefaultAsync(r => r.Ad == ad, ct);
    }

    public async Task UpsertAsync(string ad, Action<YetkiGrup> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.YetkiGruplari.FirstOrDefaultAsync(r => r.Ad == ad, ct);
        var isNew = row is null;
        row ??= new YetkiGrup { Ad = ad };
        apply(row);
        if (isNew) db.YetkiGruplari.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{ad}' yetki grubu zaten kayıtlı (eşzamanlı yazım).");
        }
    }

    public async Task<bool> DeleteAsync(string ad, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.YetkiGruplari.FirstOrDefaultAsync(r => r.Ad == ad, ct);
        if (row is null) return false;
        db.YetkiGruplari.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
