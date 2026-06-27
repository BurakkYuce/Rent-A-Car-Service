using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.EkHizmetler;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IEkHizmetTanimRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class EkHizmetTanimRepository(IDbContextFactory<AppDbContext> factory) : IEkHizmetTanimRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<EkHizmetTanim>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.EkHizmetTanimlari.AsNoTracking().OrderBy(x => x.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EkHizmetTanim>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.EkHizmetTanimlari.AsNoTracking().Where(x => x.Aktif).OrderBy(x => x.Ad).ToListAsync(ct);
    }

    public async Task<EkHizmetTanim?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.EkHizmetTanimlari.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.EkHizmetTanimlari.AsNoTracking()
            .Where(x => x.Kod == k && (excludeId == null || x.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(EkHizmetTanim tanim, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.EkHizmetTanimlari.Add(tanim);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{tanim.Kod}' kodlu ek hizmet zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<EkHizmetTanim> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var t = await db.EkHizmetTanimlari.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return false;

        apply(t);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{t.Kod}' kodlu ek hizmet zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var t = await db.EkHizmetTanimlari.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return false;

        db.EkHizmetTanimlari.Remove(t);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
