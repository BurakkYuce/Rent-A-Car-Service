using Microsoft.EntityFrameworkCore;
using RentACar.Application.Crm;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>CRM anket repo'su (roadmap C3). Tenant izolasyonu RLS + query filter ile otomatik.</summary>
public sealed class AnketRepository(IDbContextFactory<AppDbContext> factory) : IAnketRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Anket>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Anketler.AsNoTracking().OrderByDescending(r => r.Tarih).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Anket>> ListAsync(AnketFilter filtre, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Anketler.AsNoTracking();

        if (filtre.CariId is Guid cid) q = q.Where(x => x.CariId == cid);
        if (filtre.AnketTuru is { } t) q = q.Where(x => x.AnketTuru == t);
        if (filtre.Durum is { } d) q = q.Where(x => x.Durum == d);
        if (filtre.TarihMin is { } min) q = q.Where(x => x.Tarih >= min);
        if (filtre.TarihMax is { } max) q = q.Where(x => x.Tarih <= max);
        if (!string.IsNullOrWhiteSpace(filtre.CikisOfisi))
        {
            var o = filtre.CikisOfisi.Trim();
            q = q.Where(x => x.CikisOfisi != null && x.CikisOfisi.Trim() == o);
        }
        return await q.OrderByDescending(r => r.Tarih).ToListAsync(ct);
    }

    public async Task<Anket?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Anketler.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<AnketCevap>> ListCevapAsync(Guid anketId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AnketCevaplari.AsNoTracking()
            .Where(x => x.AnketId == anketId).OrderBy(x => x.SoruNo).ToListAsync(ct);
    }

    public async Task CreateWithCevapAsync(Anket anket, IReadOnlyList<AnketCevap> cevaplar, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Anketler.Add(anket);
        foreach (var c in cevaplar) { c.AnketId = anket.Id; db.AnketCevaplari.Add(c); }
        // TEK transaction: anket yazılıp cevapları yazılamazsa yarım anket kalırdı.
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> UpdateWithCevapAsync(Guid id, Action<Anket> apply,
        IReadOnlyList<AnketCevap> cevaplar, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var row = await db.Anketler.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        apply(row);

        // Cevaplar TAMAMEN değiştirilir: kısmi güncelleme yapsaydık formdan kaldırılan soru
        // eski cevabıyla kalır ve anket ekranda görünmeyen bir satır taşırdı.
        var eski = await db.AnketCevaplari.Where(x => x.AnketId == id).ToListAsync(ct);
        db.AnketCevaplari.RemoveRange(eski);
        foreach (var c in cevaplar) { c.AnketId = id; db.AnketCevaplari.Add(c); }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task CreateAsync(Anket row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Anketler.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Anket> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Anketler.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Anketler.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        db.Anketler.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// F7.1 — <see cref="UpdateWithCevapAsync"/> satır kilidi + iyimser sürüm altında (<see cref="SatirSurumu"/>):
    /// eski cevaplar kilitli işlem içinde okunur, silinir ve yenileri aynı SaveChanges'ta yazılır.
    /// </summary>
    public Task<bool> UpdateWithAnswersAsync(Guid id, string expectedVersion, Action<Anket> apply,
        IReadOnlyList<AnketCevap> answers, CancellationToken ct = default)
    {
        AppDbContext? context = null;
        List<AnketCevap> old = [];
        return SatirSurumu.GuncelleAsync(_factory, SatirSurumu.Surveys, id, expectedVersion,
            async (db, key, c) =>
            {
                var row = await db.Anketler.FirstOrDefaultAsync(r => r.Id == key, c);
                context = db;
                old = row is null ? [] : await db.AnketCevaplari.Where(x => x.AnketId == key).ToListAsync(c);
                return row;
            },
            row =>
            {
                apply(row);
                context!.AnketCevaplari.RemoveRange(old);
                foreach (var a in answers) { a.AnketId = id; context.AnketCevaplari.Add(a); }
            }, ct);
    }

    public async Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SatirSurumu.OkuAsync(db, SatirSurumu.Surveys, id, ct);
    }
}

/// <summary>CRM şikayet repo'su (roadmap C3). Tenant izolasyonu RLS + query filter ile otomatik.</summary>
public sealed class SikayetRepository(IDbContextFactory<AppDbContext> factory) : ISikayetRepository
{
    /// <summary>
    /// FAZ-43 — filtreli şikayet listesi. Plaka/sözleşme no SNAPSHOT DEĞİL: sözleşme→araç bağından
    /// her istekte okunur (araç değişirse liste doğru kalır). Sözleşmesiz şikayet DÜŞMEZ → LEFT JOIN.
    /// </summary>
    public async Task<IReadOnlyList<SikayetSatirDto>> SearchAsync(
        SikayetFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q =
            from s in db.Sikayetler.AsNoTracking()
            join c in db.Customers.AsNoTracking() on s.CariId equals (Guid?)c.Id into cg
            from c in cg.DefaultIfEmpty()
            join r in db.Rentals.AsNoTracking() on s.RentalId equals (Guid?)r.Id into rg
            from r in rg.DefaultIfEmpty()
            join v in db.Vehicles.AsNoTracking() on (Guid?)r.VehicleId equals (Guid?)v.Id into vg
            from v in vg.DefaultIfEmpty()
            join pa in db.Personeller.AsNoTracking() on s.TeslimAlanPersonelId equals (Guid?)pa.Id into pag
            from pa in pag.DefaultIfEmpty()
            join pe in db.Personeller.AsNoTracking() on s.TeslimEdenPersonelId equals (Guid?)pe.Id into peg
            from pe in peg.DefaultIfEmpty()
            select new { s, c, r, v, pa, pe };

        if (filter is not null)
        {
            if (filter.CariId is { } cid) q = q.Where(x => x.s.CariId == cid);
            if (filter.Yer is { } yer) q = q.Where(x => x.s.SikayetYeri == yer);
            if (filter.Durum is { } d) q = q.Where(x => x.s.Durum == d);
            if (!string.IsNullOrWhiteSpace(filter.Ofis))
            {
                var o = filter.Ofis.Trim();
                q = q.Where(x => x.s.CikisOfisi != null && x.s.CikisOfisi.Trim() == o);
            }
            if (!string.IsNullOrWhiteSpace(filter.Kanal))
            {
                var k = filter.Kanal.Trim();
                q = q.Where(x => x.s.SikayetKanali != null && EF.Functions.ILike(x.s.SikayetKanali, k));
            }
            if (!string.IsNullOrWhiteSpace(filter.Ara))
            {
                var a = filter.Ara.Trim();
                var p = a.ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(x => EF.Functions.ILike(x.s.Konu, $"%{a}%")
                              || (x.s.Detay != null && EF.Functions.ILike(x.s.Detay, $"%{a}%"))
                              || (x.r != null && EF.Functions.ILike(x.r.SozlesmeNo, $"%{a}%"))
                              || (x.v != null && EF.Functions.ILike(x.v.Plaka, $"%{p}%")));
            }
        }

        var limit = Math.Clamp(filter?.EnFazla ?? 1000, 1, 10000);
        var rows = await q.OrderByDescending(x => x.s.Tarih).Take(limit)
            .Select(x => new
            {
                x.s,
                MusteriAd = x.c == null ? null : (x.c.Tip == CariType.Bireysel
                    ? ((x.c.Ad ?? "") + " " + (x.c.Soyad ?? "")) : x.c.Unvan),
                MusteriTel = x.c == null ? null : x.c.CepTel,
                SozlesmeNo = x.r == null ? null : x.r.SozlesmeNo,
                Plaka = x.v == null ? null : x.v.Plaka,
                TeslimAlanAd = x.pa == null ? null : (x.pa.Ad + " " + x.pa.Soyad),
                TeslimEdenAd = x.pe == null ? null : (x.pe.Ad + " " + x.pe.Soyad)
            })
            .ToListAsync(ct);

        return rows.Select(x => new SikayetSatirDto(
            x.s, string.IsNullOrWhiteSpace(x.MusteriAd) ? null : x.MusteriAd!.Trim(), x.MusteriTel,
            x.SozlesmeNo, x.Plaka, x.TeslimAlanAd, x.TeslimEdenAd)).ToList();
    }

    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Sikayet>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Sikayetler.AsNoTracking().OrderByDescending(r => r.Tarih).ToListAsync(ct);
    }

    public async Task<Sikayet?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Sikayetler.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task CreateAsync(Sikayet row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Sikayetler.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Sikayet> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Sikayetler.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Sikayetler.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        db.Sikayetler.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>F7.1 — satır kilidi + iyimser sürüm (<see cref="SatirSurumu"/>).</summary>
    public Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<Sikayet> apply, CancellationToken ct = default)
        => SatirSurumu.GuncelleAsync(_factory, SatirSurumu.Complaints, id, expectedVersion,
            (db, key, c) => db.Sikayetler.FirstOrDefaultAsync(r => r.Id == key, c), apply, ct);

    public async Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SatirSurumu.OkuAsync(db, SatirSurumu.Complaints, id, ct);
    }
}
