using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Authorization;
using RentACar.Application.Kabis;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// KABİS kuyruk sorguları — iki depo uygulamasının (istek yolu / job yolu) ORTAK gövdesi. Hepsi verilen context
/// üzerinde çalışır; tenant izolasyonu query filter + RLS (context'in tenant'ı / GUC'u) ile otomatik.
/// </summary>
public static class KabisQueries
{
    /// <summary>
    /// Kira işleminin transaction'ında kuyruk kaydı ekler (outbox). Aynı (kira, tür) zaten varsa no-op; İade için
    /// "gerekli değil" olmayan Kiralama kaydı yoksa no-op. <c>SaveChanges</c> ÇAĞIRANIN (aynı transaction).
    /// </summary>
    public static async Task AddOutboxAsync(AppDbContext db, Guid rentalId, KabisOutbox outbox, CancellationToken ct)
    {
        if (await db.KabisBildirimleri.AnyAsync(k => k.KiraId == rentalId && k.Tur == outbox.Tur, ct)) return;
        if (outbox.RequiresReportedRental && !await db.KabisBildirimleri.AnyAsync(
                k => k.KiraId == rentalId && k.Tur == KabisNoticeType.Kiralama && k.Durum != KabisNoticeStatus.GerekliDegil, ct))
            return;
        db.KabisBildirimleri.Add(KabisNotificationService.NewRecord(rentalId, outbox.Tur, outbox.Kurum, outbox.OlayUtc));
    }

    public static async Task<KabisRentalSource?> LoadSourceAsync(AppDbContext db, Guid rentalId, CancellationToken ct)
    {
        var k = await db.Rentals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == rentalId, ct);
        if (k is null) return null;
        var vehicle = await db.Vehicles.AsNoTracking().Where(v => v.Id == k.VehicleId)
            .Select(v => new { v.Plaka, v.Marka, v.Tip }).FirstOrDefaultAsync(ct);
        var renter = await PartyAsync(db, k.MusteriId, ct);
        var second = k.IkinciSurucuId is Guid s ? await PartyAsync(db, s, ct) : null;
        return new KabisRentalSource(
            k.Id, k.SozlesmeNo, k.Durum, k.CikisSubeId, k.CikisOfisi, k.DonusOfisi,
            k.BasTar, k.BitTar, k.GercekDonusTar, k.CikisKm, k.DonusKm,
            vehicle?.Plaka ?? "", vehicle is null ? null : $"{vehicle.Marka} {vehicle.Tip}".Trim(),
            renter ?? new KabisPartySource(CustomerType.Bireysel, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, null),
            second, k.IkinciSurucuSerbestAd, k.IkinciSurucuSerbestSoyad);
    }

    /// <summary>Cari projeksiyonu — şifreli alanlar cipher olarak gelir (çözüm bellekte, yükü kuran yerde).</summary>
    private static Task<KabisPartySource?> PartyAsync(AppDbContext db, Guid customerId, CancellationToken ct)
        => db.Customers.AsNoTracking().Where(c => c.Id == customerId)
            .Select(c => new KabisPartySource(
                c.Tip, c.Ad, c.Soyad, c.Unvan, c.VergiNo,
                c.TcKimlikEnc, c.TcKimlik, c.PasaportNoEnc, c.PasaportNo, c.EhliyetNoEnc, c.EhliyetNo,
                c.EhliyetSinifi, c.EhliyetTarihi, c.DogumTarihi, c.Ulke, c.CepTel, c.Adres, c.Il, c.Ilce))
            .FirstOrDefaultAsync(ct);

    public static Task<KabisNotification?> FindAsync(AppDbContext db, Guid rentalId, KabisNoticeType type, CancellationToken ct)
        => db.KabisBildirimleri.AsNoTracking().FirstOrDefaultAsync(x => x.KiraId == rentalId && x.Tur == type, ct);

    public static Task<KabisNotification?> FindByIdAsync(AppDbContext db, Guid id, CancellationToken ct)
        => db.KabisBildirimleri.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public static async Task<IReadOnlyList<KabisNotification>> ListForRentalAsync(AppDbContext db, Guid rentalId, CancellationToken ct)
        => await db.KabisBildirimleri.AsNoTracking().Where(x => x.KiraId == rentalId).ToListAsync(ct);

    public static async Task<bool> TryClaimAsync(
        AppDbContext db, Guid id, DateTimeOffset now, TimeSpan lease, bool force, CancellationToken ct)
    {
        now = now.ToUniversalTime();
        var until = now + lease;
        // Sürmekte olan gönderim (sahiplenme süresi dolmamış) hiçbir modda alınamaz; zorla mod yalnız geri çekilmeyi atlar.
        var q = db.KabisBildirimleri.Where(x => x.Id == id && x.Durum == KabisNoticeStatus.Bekliyor
                                               && (x.SahiplenmeBitisUtc == null || x.SahiplenmeBitisUtc <= now));
        if (!force) q = q.Where(x => x.SonrakiDenemeUtc == null || x.SonrakiDenemeUtc <= now);
        return await q.ExecuteUpdateAsync(s => s
            .SetProperty(x => x.SonDenemeUtc, now)
            .SetProperty(x => x.SahiplenmeBitisUtc, until), ct) == 1;
    }

    public static async Task<IReadOnlyList<Guid>> DueAsync(AppDbContext db, DateTimeOffset now, int max, CancellationToken ct)
    {
        now = now.ToUniversalTime();
        return await db.KabisBildirimleri.AsNoTracking()
            .Where(x => x.Durum == KabisNoticeStatus.Bekliyor && (x.SonrakiDenemeUtc == null || x.SonrakiDenemeUtc <= now)
                        && (x.SahiplenmeBitisUtc == null || x.SahiplenmeBitisUtc <= now))
            .OrderBy(x => x.OlusturmaUtc)
            .Select(x => x.Id)
            .Take(max)
            .ToListAsync(ct);
    }

    public static async Task<IReadOnlyList<(Guid KiraId, KabisNoticeStatus KiralamaDurum, bool IptalVar)>> CancelledCandidatesAsync(
        AppDbContext db, CancellationToken ct)
    {
        var rows = await (
            from n in db.KabisBildirimleri.AsNoTracking()
            where n.Tur == KabisNoticeType.Kiralama
            join k in db.Rentals.AsNoTracking() on n.KiraId equals k.Id
            where k.Durum == RentalStatus.Iptal
            let hasCancel = db.KabisBildirimleri.Any(x => x.KiraId == n.KiraId && x.Tur == KabisNoticeType.Iptal)
            where n.Durum == KabisNoticeStatus.Bekliyor || n.Durum == KabisNoticeStatus.Hata
                  || (n.Durum == KabisNoticeStatus.Bildirildi && !hasCancel)
            select new { n.KiraId, n.Durum, hasCancel })
            .Take(200)
            .ToListAsync(ct);
        return rows.Select(r => (r.KiraId, r.Durum, r.hasCancel)).ToList();
    }

    /// <summary>Kira listesi şube şablonu (C4/C5 — <c>BranchScope.InScope</c> ile birebir).</summary>
    private static IQueryable<RentalContract> Scoped(AppDbContext db, BranchScope.BranchFilter scope)
    {
        var q = db.Rentals.AsNoTracking();
        if (scope.Unrestricted) return q;
        var kid = scope.SubeId; var kad = scope.SubeAd;
        return q.Where(r => (kid != null && r.CikisSubeId == kid)
                            || ((kid == null || r.CikisSubeId == null) && kad != null && r.CikisOfisi != null
                                && r.CikisOfisi.Trim() == kad));
    }

    public static async Task<IReadOnlyList<KabisProblemRow>> ListProblemsAsync(
        AppDbContext db, BranchScope.BranchFilter scope, int max, CancellationToken ct)
        => await (
            from n in db.KabisBildirimleri.AsNoTracking()
            where n.Durum == KabisNoticeStatus.Bekliyor || n.Durum == KabisNoticeStatus.Hata
            join k in Scoped(db, scope) on n.KiraId equals k.Id
            join v in db.Vehicles.AsNoTracking() on k.VehicleId equals v.Id into vs
            from v in vs.DefaultIfEmpty()
            orderby n.OlayUtc
            select new KabisProblemRow(n.KiraId, k.SozlesmeNo, v != null ? v.Plaka : null, n.Tur, n.Durum,
                n.DenemeSayisi, n.SonHata, n.OlayUtc, n.SonDenemeUtc))
            .Take(max)
            .ToListAsync(ct);

    public static Task<int> CountProblemRentalsAsync(AppDbContext db, BranchScope.BranchFilter scope, CancellationToken ct)
        => (from n in db.KabisBildirimleri.AsNoTracking()
            where n.Durum == KabisNoticeStatus.Bekliyor || n.Durum == KabisNoticeStatus.Hata
            join k in Scoped(db, scope) on n.KiraId equals k.Id
            select n.KiraId).Distinct().CountAsync(ct);

    /// <summary>Job ön kontrolü: kapı açık ve yapılacak iş (vadesi gelen kayıt ya da iptal uzlaştırması) var mı.</summary>
    public static async Task<bool> HasWorkAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        now = now.ToUniversalTime();
        if (!await db.TenantSettings.AsNoTracking().AnyAsync(s => s.KabisEtkin, ct)) return false;
        if (await db.KabisBildirimleri.AnyAsync(
                x => x.Durum == KabisNoticeStatus.Bekliyor && (x.SonrakiDenemeUtc == null || x.SonrakiDenemeUtc <= now)
                     && (x.SahiplenmeBitisUtc == null || x.SahiplenmeBitisUtc <= now), ct))
            return true;
        return (await CancelledCandidatesAsync(db, ct)).Count > 0;
    }

    public static async Task<bool> AddAsync(AppDbContext db, KabisNotification notification, CancellationToken ct)
    {
        db.KabisBildirimleri.Add(notification);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı (kira, tür) başka bir çağrıda yazıldı → idempotent no-op. Batch geri alındı; izleyici temizlenir.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public static async Task UpdateAsync(AppDbContext db, Guid id, Action<KabisNotification> apply, CancellationToken ct)
    {
        var row = await db.KabisBildirimleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return;
        apply(row);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>İstek yolu deposu: her çağrı kısa ömürlü context (factory; tenant + RLS GUC interceptor'dan).</summary>
public sealed class KabisRepository(IDbContextFactory<AppDbContext> factory) : IKabisRepository
{
    private async Task<T> With<T>(Func<AppDbContext, Task<T>> f, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await f(db);
    }

    public Task<KabisRentalSource?> LoadSourceAsync(Guid rentalId, CancellationToken ct = default)
        => With(db => KabisQueries.LoadSourceAsync(db, rentalId, ct), ct);

    public Task<KabisNotification?> FindAsync(Guid rentalId, KabisNoticeType type, CancellationToken ct = default)
        => With(db => KabisQueries.FindAsync(db, rentalId, type, ct), ct);

    public Task<KabisNotification?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => With(db => KabisQueries.FindByIdAsync(db, id, ct), ct);

    public Task<IReadOnlyList<KabisNotification>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default)
        => With(db => KabisQueries.ListForRentalAsync(db, rentalId, ct), ct);

    public Task<bool> AddAsync(KabisNotification notification, CancellationToken ct = default)
        => With(db => KabisQueries.AddAsync(db, notification, ct), ct);

    public Task<bool> TryClaimAsync(Guid id, DateTimeOffset now, TimeSpan lease, bool force, CancellationToken ct = default)
        => With(db => KabisQueries.TryClaimAsync(db, id, now, lease, force, ct), ct);

    public Task UpdateAsync(Guid id, Action<KabisNotification> apply, CancellationToken ct = default)
        => With(async db => { await KabisQueries.UpdateAsync(db, id, apply, ct); return true; }, ct);

    public Task<IReadOnlyList<Guid>> DueAsync(DateTimeOffset now, int max, CancellationToken ct = default)
        => With(db => KabisQueries.DueAsync(db, now, max, ct), ct);

    public Task<IReadOnlyList<(Guid KiraId, KabisNoticeStatus KiralamaDurum, bool IptalVar)>> CancelledRentalCandidatesAsync(
        CancellationToken ct = default)
        => With(db => KabisQueries.CancelledCandidatesAsync(db, ct), ct);

    public Task<IReadOnlyList<KabisProblemRow>> ListProblemsAsync(BranchScope.BranchFilter scope, int max, CancellationToken ct = default)
        => With(db => KabisQueries.ListProblemsAsync(db, scope, max, ct), ct);

    public Task<int> CountProblemRentalsAsync(BranchScope.BranchFilter scope, CancellationToken ct = default)
        => With(db => KabisQueries.CountProblemRentalsAsync(db, scope, ct), ct);
}
