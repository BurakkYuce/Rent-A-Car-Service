using Microsoft.EntityFrameworkCore;
using RentACar.Application.Crm;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Assistans (yol yardım) talebi kalıcılığı — FAZ-44.</summary>
public sealed class AssistanceRequestRepository(IDbContextFactory<AppDbContext> factory) : IAssistanceRequestRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<AssistansTalep>> SearchAsync(
        AssistansFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.AssistansTalepleri.AsNoTracking();

        if (filter.TarihMin is { } min) q = q.Where(x => x.Zaman >= min);
        if (filter.TarihMax is { } max) q = q.Where(x => x.Zaman <= max);
        if (filter.Kapandi is bool k) q = q.Where(x => x.Kapandi == k);
        if (filter.YedekLastikMi is bool y) q = q.Where(x => x.YedekLastikMi == y);
        // "Hareket edemiyor" = AracHareketMi FALSE — çekici gereken çağrılar. Kolonun adı olumlu
        // olduğu için filtrenin adı da olumsuz seçildi; ekranda operatör "çekici gerekenler" arar.
        if (filter.HareketEdemiyor is bool h) q = q.Where(x => x.AracHareketMi == !h);

        if (!string.IsNullOrWhiteSpace(filter.Plaka))
        {
            // Plaka DB'de normalize (34AA01); kullanıcı "34 AA" yazabilir → arama terimi de
            // normalize edilir, yoksa boşluklu giriş hiçbir şey bulmaz (FAZ-63 dersi).
            var p = AssistanceRequestService.NormalizePlate(filter.Plaka);
            if (p.Length > 0) q = q.Where(x => x.Plaka != null && x.Plaka.Contains(p));
        }

        if (!string.IsNullOrWhiteSpace(filter.Ara))
        {
            var a = filter.Ara.Trim();
            // #283 KVKK M1: the name/phone snapshot may have been copied from the linked rental's customer; when that
            // customer is anonymised the snapshot is hidden on screen, so it must not be matchable either.
            q = q.Where(x => EF.Functions.ILike(x.Mesaj, $"%{a}%")
                          || (x.Sebep != null && EF.Functions.ILike(x.Sebep, $"%{a}%"))
                          || (x.AdSoyad != null && EF.Functions.ILike(x.AdSoyad, $"%{a}%")
                              && !db.Rentals.Any(r => r.Id == x.RentalId
                                  && db.Customers.Any(c => c.Id == r.MusteriId && c.AnonimAd)))
                          || (x.CepTel != null && EF.Functions.ILike(x.CepTel, $"%{a}%")
                              && !db.Rentals.Any(r => r.Id == x.RentalId
                                  && db.Customers.Any(c => c.Id == r.MusteriId && c.AnonimTelefon))));
        }

        return await q.OrderByDescending(x => x.Zaman).Take(10_000).ToListAsync(ct); // #283 L2: upper bound
    }

    public async Task<AssistansTalep?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AssistansTalepleri.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(AssistansTalep row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.AssistansTalepleri.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<AssistansTalep> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AssistansTalepleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AssistansTalepleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        db.AssistansTalepleri.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>F7.1 — satır kilidi + iyimser sürüm (<see cref="RowVersionSql"/>).</summary>
    public Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<AssistansTalep> apply, CancellationToken ct = default)
        => RowVersionSql.UpdateAsync(_factory, RowVersionSql.AssistanceRequests, id, expectedVersion,
            (db, key, c) => db.AssistansTalepleri.FirstOrDefaultAsync(x => x.Id == key, c), apply, ct);

    public async Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await RowVersionSql.ReadAsync(db, RowVersionSql.AssistanceRequests, id, ct);
    }
}
