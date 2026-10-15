using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Hgs;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence.Configurations;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// HGS geçiş kalıcılığı. İçe aktarma, yeniden eşleştirme ve yansıtma AYNI kiracı-kapsamlı danışma kilidini alır
/// (<c>hgs:{kiracı}</c>) → birbirleriyle yarışmazlar. Yansıtma ayrıca satır kilidi (FOR UPDATE) alır ve durum
/// değişikliğini defter kümesiyle aynı transaction'da yazar. Son savunmalar DB'dedir: doğal anahtar unique index'i
/// (mükerrer geçiş), defterdeki 'Hgs' kısmi unique index'i (bir geçiş bir kez borçlanır) ve yansıtılmış satırın
/// değişmezlik trigger'ı.
/// </summary>
public sealed class HgsCrossingRepository(IDbContextFactory<AppDbContext> factory) : IHgsCrossingRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<HgsVehicleRef>> ListVehiclesAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().Select(v => new HgsVehicleRef(v.Id, v.Plaka)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<HgsRentalWindow>> ListRentalWindowsAsync(
        IReadOnlyCollection<Guid> vehicleIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (vehicleIds.Count == 0) return [];
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await WindowsAsync(db, vehicleIds, from, to, ct);
    }

    /// <summary>Kira → eşleştirme penceresi: teslim = BasTar; dönüş = GercekDonusTar, yoksa açık kirada sınırsız,
    /// kapanmış (dönüşü girilmemiş) kirada planlanan BitTar. İptal kira hiç pencere üretmez.</summary>
    private static async Task<List<HgsRentalWindow>> WindowsAsync(
        AppDbContext db, IReadOnlyCollection<Guid> vehicleIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var ids = vehicleIds.ToList();
        var rows = await db.Rentals.AsNoTracking()
            .Where(r => ids.Contains(r.VehicleId) && r.Durum != RentalStatus.Iptal && r.BasTar <= to
                        && (r.GercekDonusTar == null || r.GercekDonusTar > from))
            .Select(r => new { r.Id, r.VehicleId, r.MusteriId, r.SozlesmeNo, r.BasTar, r.BitTar, r.GercekDonusTar, r.Durum })
            .ToListAsync(ct);
        return rows
            .Select(r => new HgsRentalWindow(r.Id, r.VehicleId, r.MusteriId, r.SozlesmeNo, r.BasTar,
                r.GercekDonusTar ?? (r.Durum == RentalStatus.Kirada ? null : r.BitTar)))
            .Where(w => w.End is null || w.End > from)
            .ToList();
    }

    private static HgsRentalWindow WindowOf(RentalContract r)
        => new(r.Id, r.VehicleId, r.MusteriId, r.SozlesmeNo, r.BasTar,
            r.GercekDonusTar ?? (r.Durum == RentalStatus.Kirada ? null : r.BitTar));

    public async Task<IReadOnlySet<string>> ExistingKeysAsync(
        IReadOnlyCollection<string> plates, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await KeysAsync(db, plates, from, to, ct);
    }

    private static async Task<HashSet<string>> KeysAsync(
        AppDbContext db, IReadOnlyCollection<string> plates, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var list = plates.Distinct().ToList();
        if (list.Count == 0) return new HashSet<string>(StringComparer.Ordinal);
        var rows = await db.HgsGecisleri.AsNoTracking()
            .Where(c => list.Contains(c.Plaka) && c.GecisZamani >= from && c.GecisZamani <= to)
            .Select(c => new { c.Plaka, c.GecisZamani, c.IstasyonAnahtari, c.Tutar })
            .ToListAsync(ct);
        return rows.Select(c => HgsResolution.NaturalKey(c.Plaka, c.GecisZamani, c.IstasyonAnahtari, c.Tutar))
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlySet<string>> InsertAsync(IReadOnlyList<HgsCrossing> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return new HashSet<string>(StringComparer.Ordinal);
        return await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockAsync(db, ct);
            // Önizlemeden bu yana başka bir yükleme aynı geçişleri eklemiş olabilir: kilit ALTINDA yeniden ele.
            var existing = await KeysAsync(db, rows.Select(r => r.Plaka).ToList(),
                rows.Min(r => r.GecisZamani), rows.Max(r => r.GecisZamani), ct);
            var skipped = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                var key = HgsResolution.NaturalKey(r);
                if (existing.Contains(key)) skipped.Add(key);
                else db.HgsGecisleri.Add(r);
            }
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                               { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: HgsCrossingConfig.NaturalKeyIndex })
            {
                // Kilidi almayan bir yazıcı (ileride API) ile yarış: hiçbiri yazılmadı, kullanıcı yeniden dener.
                throw new ValidationException("Aynı geçişler eşzamanlı olarak içe aktarıldı; önizlemeyi yenileyip tekrar deneyin.");
            }
            return (IReadOnlySet<string>)skipped;
        }, ct);
    }

    public async Task<IReadOnlyList<HgsCrossing>> ListAsync(HgsCrossingFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.HgsGecisleri.AsNoTracking().AsQueryable();
        if (filter.RentalId is { } rental) q = q.Where(c => c.RentalId == rental);
        if (filter.IceAktarimId is { } batch) q = q.Where(c => c.IceAktarimId == batch);
        if (filter.Durum is { } state) q = q.Where(c => c.Durum == state);
        if (filter.Bas is { } from) q = q.Where(c => c.GecisZamani >= from);
        if (filter.Bit is { } to) q = q.Where(c => c.GecisZamani < to);
        if (!string.IsNullOrWhiteSpace(filter.Plaka))
        {
            var key = HgsValueParser.PlateMatchKey(filter.Plaka);
            q = q.Where(c => c.Plaka.Contains(key));
        }
        return await q.OrderByDescending(c => c.GecisZamani).ThenBy(c => c.Id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<HgsCrossing>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.HgsGecisleri.AsNoTracking().Where(c => list.Contains(c.Id)).ToListAsync(ct);
    }

    public async Task<int> RematchAsync(CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockAsync(db, ct);
            var open = await db.HgsGecisleri.Where(c => c.Durum != HgsCrossingStatus.Yansitildi).ToListAsync(ct);
            if (open.Count == 0) return 0;
            var vehicles = HgsResolution.VehicleIndex(
                await db.Vehicles.AsNoTracking().Select(v => new HgsVehicleRef(v.Id, v.Plaka)).ToListAsync(ct));
            var vehicleIds = open.Select(c => vehicles.GetValueOrDefault(c.Plaka)).OfType<Guid>().Distinct().ToList();
            var windows = vehicleIds.Count == 0
                ? Enumerable.Empty<HgsRentalWindow>().ToLookup(w => w.VehicleId)
                : (await WindowsAsync(db, vehicleIds, open.Min(c => c.GecisZamani), open.Max(c => c.GecisZamani), ct))
                    .ToLookup(w => w.VehicleId);
            var now = DateTimeOffset.UtcNow;
            var changed = 0;
            foreach (var c in open)
            {
                var (vehicleId, match) = HgsResolution.Resolve(c.Plaka, c.GecisZamani, vehicles, windows);
                var rentalId = match.Rental?.RentalId;
                if (c.VehicleId == vehicleId && c.RentalId == rentalId && c.Durum == match.Status) continue;
                c.VehicleId = vehicleId;
                c.RentalId = rentalId;
                c.Durum = match.Status;
                c.UpdatedAtUtc = now;
                changed++;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return changed;
        }, ct);
    }

    public async Task<HgsReflectResult> ReflectAsync(
        IReadOnlyCollection<Guid> ids, Func<HgsCrossing, HgsRentalWindow, HgsReflectionPlan> plan,
        CancellationToken ct = default)
    {
        var idArray = ids.Distinct().ToArray();
        return await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockAsync(db, ct); // danışma → satır (içe aktarma/yeniden eşleştirmeyle aynı sıra)
            var rows = await db.HgsGecisleri
                .FromSqlRaw("SELECT * FROM \"HgsGecisleri\" WHERE \"Id\" = ANY({0}) FOR UPDATE", idArray)
                .OrderBy(c => c.GecisZamani).ThenBy(c => c.Id)
                .ToListAsync(ct);
            var rentalIds = rows.Where(c => c.RentalId is not null).Select(c => c.RentalId!.Value).Distinct().ToList();
            var rentals = await db.Rentals.AsNoTracking().Where(r => rentalIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);

            int reflected = 0, already = 0, unfit = idArray.Length - rows.Count; // bulunamayan (başka kiracı/yok) = uygunsuz
            decimal crossingTotal = 0m, reflectedTotal = 0m;
            var entries = new List<AccountLedgerEntry>();
            var now = DateTimeOffset.UtcNow;
            foreach (var c in rows)
            {
                if (c.Durum == HgsCrossingStatus.Yansitildi) { already++; continue; }
                // Kilit altında GÜNCEL kira ile yeniden doğrula: kira iptal edildiyse, aracı değiştiyse ya da dönüş
                // anı düzeltilip geçiş artık aralıkta değilse müşteri borçlandırılmaz (yeniden eşleştirme gerekir).
                if (c.Durum != HgsCrossingStatus.Eslesti || c.RentalId is not { } rid
                    || !rentals.TryGetValue(rid, out var rental) || rental.Durum == RentalStatus.Iptal
                    || rental.VehicleId != c.VehicleId)
                {
                    unfit++;
                    continue;
                }
                var window = WindowOf(rental);
                if (HgsCrossingMatcher.Match(c.GecisZamani, [window]).Status != HgsCrossingStatus.Eslesti)
                {
                    unfit++;
                    continue;
                }
                var p = plan(c, window);
                var debit = p.Entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
                var credit = p.Entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
                if (debit != credit || debit != p.Amount)
                    throw new ValidationException($"HGS yansıtma defteri dengesiz: borç {debit} ≠ alacak {credit}.");
                c.Durum = HgsCrossingStatus.Yansitildi;
                c.HizmetOrani = p.Rate;
                c.YansitilanTutar = p.Amount;
                c.YansitmaZamani = p.Entries[0].EntryDateUtc;
                c.YansitilanCariId = window.CustomerId;
                c.UpdatedAtUtc = now;
                entries.AddRange(p.Entries);
                reflected++;
                crossingTotal += c.Tutar;
                reflectedTotal += p.Amount;
            }
            db.AccountLedgerEntries.AddRange(entries);
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Kilit + durum kontrolü varken buraya düşmek, geçişin defterde ZATEN 'Hgs' kümesi olduğu anlamına gelir
                // (ör. eski dönem-MD5 yolu ile aynı kimlik — pratikte imkânsız). Hiçbir şey yazılmadı.
                throw IdempotencyConstraint.Red(ex, "Seçili geçişlerden biri defterde zaten yansıtılmış.");
            }
            return new HgsReflectResult(reflected, already, unfit, crossingTotal, reflectedTotal);
        }, ct);
    }

    /// <summary>Kiracı-kapsamlı HGS danışma kilidi (<c>pg_advisory_xact_lock</c>; transaction bitince bırakılır).</summary>
    private static async Task LockAsync(AppDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@k, 42))";
        var p = cmd.CreateParameter();
        p.ParameterName = "k";
        p.Value = string.Create(CultureInfo.InvariantCulture, $"hgs:{db.TenantId}");
        cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync(ct);
    }
}
