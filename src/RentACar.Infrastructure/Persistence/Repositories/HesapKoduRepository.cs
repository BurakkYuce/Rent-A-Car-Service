using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.HesapKodlari;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Hesap kodu kalıcılığı (roadmap N1). Kod benzersizliği DB unique index; 23505→ValidationException.</summary>
public sealed class HesapKoduRepository(IDbContextFactory<AppDbContext> factory) : IHesapKoduRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<HesapKodu>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.HesapKodlari.AsNoTracking().OrderBy(c => c.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<HesapKodu>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.HesapKodlari.AsNoTracking().Where(c => c.Aktif).OrderBy(c => c.Ad).ToListAsync(ct);
    }

    public async Task<HesapKodu?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.HesapKodlari.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task CreateAsync(HesapKodu row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.HesapKodlari.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException($"'{row.Kod}' kodlu hesap zaten var."); }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<HesapKodu> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.HesapKodlari.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return false;
        apply(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException($"'{row.Kod}' kodlu hesap zaten var."); }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.HesapKodlari.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return false;
        db.HesapKodlari.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
