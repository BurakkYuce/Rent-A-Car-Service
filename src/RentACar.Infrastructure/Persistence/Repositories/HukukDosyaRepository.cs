using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Legal;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IHukukDosyaRepository: kısa-ömürlü context (factory). Tenant izolasyonu RLS + query filter. DosyaNo
/// benzersizliği DB unique index; ihlal (23505) ValidationException.
/// </summary>
public sealed class HukukDosyaRepository(IDbContextFactory<AppDbContext> factory) : ILegalCaseRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<HukukDosya>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.HukukDosyalari.AsNoTracking().OrderByDescending(r => r.Tarih).ToListAsync(ct);
    }

    /// <summary>
    /// FAZ-41 — filtreli liste. Müşteri adı/telefonu SNAPSHOT DEĞİL: cari bağından her istekte
    /// okunur (cari yeniden adlandırılırsa liste doğru kalır). Carisiz dosya DÜŞMEZ → LEFT JOIN.
    /// </summary>
    public async Task<IReadOnlyList<HukukDosyaSatirDto>> SearchAsync(
        HukukDosyaFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q =
            from h in db.HukukDosyalari.AsNoTracking()
            join c in db.Customers.AsNoTracking() on h.CariId equals (Guid?)c.Id into cg
            from c in cg.DefaultIfEmpty()
            select new { h, c };

        if (filter is not null)
        {
            if (filter.CariId is { } cid) q = q.Where(x => x.h.CariId == cid);
            if (filter.Tur is { } tur) q = q.Where(x => x.h.Tur == tur);
            if (filter.Durum is { } dur) q = q.Where(x => x.h.Durum == dur);
            if (filter.Bas is { } bas) q = q.Where(x => x.h.Tarih >= bas);
            if (filter.Bit is { } bit) q = q.Where(x => x.h.Tarih <= bit);
            if (!string.IsNullOrWhiteSpace(filter.FaturaNo))
            {
                var f = filter.FaturaNo.Trim();
                q = q.Where(x => x.h.FaturaNoTemp != null && EF.Functions.ILike(x.h.FaturaNoTemp, $"%{f}%"));
            }
            if (!string.IsNullOrWhiteSpace(filter.DosyaNo))
            {
                var d = filter.DosyaNo.Trim();
                q = q.Where(x => EF.Functions.ILike(x.h.DosyaNo, $"%{d}%"));
            }
            if (!string.IsNullOrWhiteSpace(filter.Ara))
            {
                var a = filter.Ara.Trim();
                q = q.Where(x => (x.h.Avukat != null && EF.Functions.ILike(x.h.Avukat, $"%{a}%"))
                              || (x.h.Avukat2Ad != null && EF.Functions.ILike(x.h.Avukat2Ad, $"%{a}%"))
                              || (x.h.Aciklama != null && EF.Functions.ILike(x.h.Aciklama, $"%{a}%")));
            }
        }

        var limit = Math.Clamp(filter?.EnFazla ?? 1000, 1, 10000);
        var rows = await q.OrderByDescending(x => x.h.Tarih).Take(limit)
            .Select(x => new
            {
                x.h,
                MusteriAd = x.c == null ? null : (x.c.Tip == CustomerType.Bireysel
                    ? ((x.c.Ad ?? "") + " " + (x.c.Soyad ?? "")) : x.c.Unvan),
                MusteriTel = x.c == null ? null : x.c.CepTel
            })
            .ToListAsync(ct);

        return rows.Select(x => new HukukDosyaSatirDto(
            x.h, string.IsNullOrWhiteSpace(x.MusteriAd) ? null : x.MusteriAd!.Trim(), x.MusteriTel)).ToList();
    }

    public async Task<HukukDosya?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.HukukDosyalari.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> FileNoExistsAsync(string dosyaNo, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = dosyaNo.Trim().ToUpperInvariant();
        return await db.HukukDosyalari.AsNoTracking()
            .Where(r => r.DosyaNo == k && (excludeId == null || r.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(HukukDosya row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.HukukDosyalari.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.DosyaNo}' dosya no zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<HukukDosya> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.HukukDosyalari.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        apply(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.DosyaNo}' dosya no zaten var.");
        }
        return true;
    }

    /// <summary>F7.1 — satır kilidi + iyimser sürüm (<see cref="SatirSurumu"/>); DosyaNo yarışı yine 400.</summary>
    public async Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<HukukDosya> apply, CancellationToken ct = default)
    {
        string? fileNo = null;
        try
        {
            return await SatirSurumu.GuncelleAsync(_factory, SatirSurumu.LegalFiles, id, expectedVersion,
                (db, key, c) => db.HukukDosyalari.FirstOrDefaultAsync(r => r.Id == key, c),
                r => { apply(r); fileNo = r.DosyaNo; }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{fileNo}' dosya no zaten var.");
        }
    }

    public async Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SatirSurumu.OkuAsync(db, SatirSurumu.LegalFiles, id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.HukukDosyalari.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        db.HukukDosyalari.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
