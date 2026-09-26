using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.RentalRules;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IRentalRuleRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter ile
/// otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class RentalRuleRepository(IDbContextFactory<AppDbContext> factory) : IRentalRuleRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<RentalRule>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RentalRules.AsNoTracking().OrderBy(r => r.Kod).ToListAsync(ct);
    }

    /// <summary>
    /// Fiyat motorunu besleyen küme. FAZ-73: koşul <c>Aktif == true</c> yerine
    /// <c>KampanyaDurum == Aktif</c> okur — bu, kuralın motorda seçilip seçilmeyeceğine karar veren
    /// TEK noktadır (wire-in taraması: <c>.Aktif</c>'i okuyan başka üretim yolu yok).
    /// Migration backfill'i <c>Aktif=true → Aktif</c>, <c>Aktif=false → Pasif</c> eşlediğinden
    /// mevcut kayıtlar için sonuç kümesi göç öncesiyle BİREBİR aynıdır.
    /// </summary>
    public async Task<IReadOnlyList<RentalRule>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RentalRules.AsNoTracking()
            .Where(r => r.KampanyaDurum == CampaignStatus.Aktif).OrderBy(r => r.Ad).ToListAsync(ct);
    }

    public async Task<RentalRule?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RentalRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> CodeExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.RentalRules.AsNoTracking()
            .Where(r => r.Kod == k && (excludeId == null || r.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(RentalRule row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.RentalRules.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Kod}' kodlu kiralama kuralı zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<RentalRule> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.RentalRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        apply(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Kod}' kodlu kiralama kuralı zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.RentalRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        db.RentalRules.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
