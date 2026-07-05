using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Kur sabitleme CRUD (SabitKurlar — tenant-owned; izolasyon RLS + query filter ile otomatik). Kod
/// benzersizliği DB unique index; ihlal (23505) ValidationException.
/// </summary>
public sealed class SabitKurRepository(IDbContextFactory<AppDbContext> factory) : ISabitKurRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<SabitKur>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SabitKurlar.AsNoTracking().OrderBy(x => x.Kod).ToListAsync(ct);
    }

    public async Task<SabitKur?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SabitKurlar.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<SabitKur?> GetActiveAsync(string kod, DateTimeOffset tarih, CancellationToken ct = default)
    {
        var k = kod.Trim().ToUpperInvariant();
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SabitKurlar.AsNoTracking()
            .Where(x => x.Kod == k && x.Aktif
                && (x.BasTar == null || x.BasTar <= tarih)
                && (x.BitTar == null || tarih <= x.BitTar))
            .OrderByDescending(x => x.BasTar)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId, CancellationToken ct = default)
    {
        var k = kod.Trim().ToUpperInvariant();
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SabitKurlar.AsNoTracking()
            .Where(x => x.Kod == k && (excludeId == null || x.Id != excludeId)).AnyAsync(ct);
    }

    public async Task CreateAsync(SabitKur sabit, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.SabitKurlar.Add(sabit);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException($"'{sabit.Kod}' için sabit kur zaten var."); }
    }

    public async Task<bool> UpdateAsync(SabitKur sabit, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var mevcut = await db.SabitKurlar.FirstOrDefaultAsync(x => x.Id == sabit.Id, ct);
        if (mevcut is null) return false;
        mevcut.Kod = sabit.Kod;
        mevcut.Kur = sabit.Kur;
        mevcut.BasTar = sabit.BasTar;
        mevcut.BitTar = sabit.BitTar;
        mevcut.Aktif = sabit.Aktif;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var s = await db.SabitKurlar.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return false;
        db.SabitKurlar.Remove(s);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
