using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Nakit işlem + defter kalıcılığı. PostAsync: No tahsisi + belge + dengeli defter
/// kümesi + (kira) Tahsilat/Bakiye → TEK transaction. Dengelilik (Σ borç = Σ alacak,
/// base) burada da doğrulanır (yapısal invariant guard).
/// </summary>
public sealed class CashRepository(IDbContextFactory<AppDbContext> factory) : ICashRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<CashTransaction>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.CashTransactions.AsNoTracking().OrderByDescending(t => t.Tarih).ToListAsync(ct);
    }

    public async Task<CashTransaction?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.CashTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<bool> HasReversalAsync(Guid originalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.CashTransactions.AsNoTracking().AnyAsync(t => t.TersAlinanId == originalId, ct);
    }

    /// <summary>
    /// Kira tahsilat deltası — KİRA DÖVİZİNDE (K2 fix). Yön = Tip(Tahsilat:+/Ödeme:−) × TersKayitMi(−).
    /// TRY kira → TL-baz (AmountInBase, mevcut davranış). FX kira → tahsilat AYNI dövizde zorunlu (ham Amount);
    /// farklı döviz karışık-birim Bakiye üretirdi (1000 EUR kira + TL-baz delta → −34.000 "alacak") → red.
    /// </summary>
    private static decimal RentalDelta(CashTransaction tx, string? kiraDoviz)
    {
        var yon = (tx.Tip == CashTransactionType.Tahsilat ? 1m : -1m) * (tx.TersKayitMi ? -1m : 1m);
        var kira = RentACar.Application.Kur.KurService.NormalizeKod(kiraDoviz);
        if (kira == "TRY") return yon * tx.Amount.AmountInBase;
        if (RentACar.Application.Kur.KurService.NormalizeKod(tx.Amount.Currency) != kira)
            throw new ValidationException($"Kira dövizi {kira}; tahsilat/iade aynı dövizde girilmelidir.");
        return yon * tx.Amount.Amount;
    }

    /// <summary>Kira Tahsilat/Bakiye'yi ATOMİK SQL ile günceller (O1 fix: eşzamanlı tahsilatta kayıp yok;
    /// SET sağ tarafı ESKİ satır değerini okur → += yarışsız). Aynı transaction içinde çağrılır; RLS geçerli.</summary>
    // FAZ 4.2-B4: DonemFaturaUretici (job) kira Tahsilat/Bakiye deltasını da BU metottan uygular (tek kopya).
    internal static async Task ApplyRentalDeltaAsync(AppDbContext db, CashTransaction tx, CancellationToken ct)
    {
        if (tx.RentalId is not Guid rentalId) return;
        var rental = await db.Rentals.AsNoTracking()
            .Where(r => r.Id == rentalId).Select(r => new { r.Doviz }).FirstOrDefaultAsync(ct);
        if (rental is null) return;
        var delta = RentalDelta(tx, rental.Doviz);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE ""Rentals"" SET
                ""Tahsilat"" = ""Tahsilat"" + {delta},
                ""Bakiye"" = ""GenelToplam"" - (""Tahsilat"" + {delta}),
                ""UpdatedAtUtc"" = {DateTimeOffset.UtcNow}
            WHERE ""Id"" = {rentalId}", ct);
    }

    public async Task PostAsync(
        CashTransaction tx, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        // Dengelilik guard: Σ Borç(base) == Σ Alacak(base).
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Defter dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var dbTx = await db.Database.BeginTransactionAsync(ct);

            var n = await SequenceAllocator.NextAsync(db, db.TenantId, "CashNo", ct);
            tx.No = $"{(tx.Tip == CashTransactionType.Odeme ? "TD" : "TH")}-{n:D6}";
            db.CashTransactions.Add(tx);
            db.AccountLedgerEntries.AddRange(entries);
            await ApplyRentalDeltaAsync(db, tx, ct); // atomik SQL += (O1); kira dövizi doğrulanır (K2)

            try
            {
                await db.SaveChangesAsync(ct);
                await dbTx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Kısmi unique index çakışması: ya aynı işlemin ikinci ters kaydı ya da aynı IslemAnahtari ile
                // çift-submit (adversarial M5) → her iki halde idempotent reddet.
                await dbTx.RollbackAsync(ct);
                RentACar.Application.Observability.RacarMetrics.LedgerIdempotentRejected(); // metrik: idempotent red
                throw new ValidationException("Bu işlem zaten kaydedilmiş (çift gönderim / mükerrer).");
            }
        }, ct);
    }

    public async Task PostDepozitoIslemAsync(
        Guid cariId, bool kontrolEt, DepozitoIrat? izKaydi,
        IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        // Dengelilik guard (PostAsync deseni).
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Defter dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var dbTx = await db.Database.BeginTransactionAsync(ct);

            // TOCTOU çiti (adversarial 1.2 Medium): (tenant, cari) danışma kilidi — bu carinin depozito
            // işlemleri tx sonuna dek sıralanır; bakiye kontrolü kilidin ARKASINDA yapılır → eşzamanlı
            // iki iade/irat toplamı tutulanı aşamaz.
            await DepozitoKilitAsync(db, cariId, ct);

            if (kontrolEt)
            {
                var rows = await db.AccountLedgerEntries.AsNoTracking()
                    .Where(e => e.AccountType == LedgerAccountType.Depozito && e.AccountRef == cariId)
                    .Select(e => new { e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
                    .ToListAsync(ct);
                var tutulan = rows.Sum(r => r.Direction == LedgerDirection.Credit ? r.A * r.R : -(r.A * r.R));
                var cikan = entries
                    .Where(e => e.AccountType == LedgerAccountType.Depozito && e.Direction == LedgerDirection.Debit)
                    .Sum(e => e.Amount.AmountInBase);
                if (cikan > tutulan)
                    throw new ValidationException($"İşlem tutarı ({cikan}) tutulan depozitoyu ({tutulan}) aşamaz.");
            }

            if (izKaydi is not null)
            {
                // Kira bağı çiti: verilen kira BU carinin olmalı (yanlış araca gelir atfı engellenir).
                if (izKaydi.RentalId is { } rid)
                {
                    var musteri = await db.Rentals.Where(r => r.Id == rid)
                        .Select(r => (Guid?)r.MusteriId).FirstOrDefaultAsync(ct)
                        ?? throw new ValidationException("İrat için verilen kira bulunamadı.");
                    if (musteri != izKaydi.CariId)
                        throw new ValidationException("İrat kirası bu cariye ait değil.");
                }
                db.DepozitoIratlar.Add(izKaydi);
            }
            db.AccountLedgerEntries.AddRange(entries);
            try
            {
                await db.SaveChangesAsync(ct);
                await dbTx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await dbTx.RollbackAsync(ct);
                // TENANT-GÖRÜNÜR teyit (adversarial 1.2 Low): aynı kayıt bu tenant'ta varsa gerçek
                // çift-submit → sessiz idempotent no-op (I3). Görünmüyorsa (çapraz-tenant PK çakışması)
                // sessiz yutmak geliri kaybettirir → net red.
                var sid = entries[0].SourceId;
                var st = entries[0].SourceType;
                var gorunur = izKaydi is not null
                    ? await db.DepozitoIratlar.AsNoTracking().AnyAsync(d => d.Id == izKaydi.Id, ct)
                    : await db.AccountLedgerEntries.AsNoTracking()
                        .AnyAsync(e => e.SourceType == st && e.SourceId == sid, ct);
                if (!gorunur)
                    throw new ValidationException("İşlem anahtarı başka bir kayıtla çakıştı — yeni anahtarla tekrar deneyin.");
            }
        }, ct);
    }

    /// <summary>(tenant, cari) kapsamlı pg_advisory_xact_lock — tx bitince otomatik bırakılır.</summary>
    private static async Task DepozitoKilitAsync(AppDbContext db, Guid cariId, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@k, 42))";
        var p = cmd.CreateParameter();
        p.ParameterName = "k";
        p.Value = $"depozito:{db.TenantId}:{cariId}";
        cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync(ct);
    }

    public async Task PostBatchAsync(IReadOnlyList<CashPosting> items, CancellationToken ct = default)
    {
        if (items.Count == 0) throw new ValidationException("Toplu işlem en az bir satır içermelidir.");

        // Her satır dengeli olmalı (yapısal invariant).
        foreach (var it in items)
        {
            var d = it.Entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
            var c = it.Entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
            if (d != c) throw new ValidationException($"Defter dengesiz: borç {d} ≠ alacak {c}.");
        }

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var dbTx = await db.Database.BeginTransactionAsync(ct);

            // ATOMİK: tüm satırlar TEK transaction'da. No'lar boşluksuz; rollback olursa sıra geri alınır.
            foreach (var it in items)
            {
                var n = await SequenceAllocator.NextAsync(db, db.TenantId, "CashNo", ct);
                it.Tx.No = $"{(it.Tx.Tip == CashTransactionType.Odeme ? "TD" : "TH")}-{n:D6}";
                db.CashTransactions.Add(it.Tx);
                db.AccountLedgerEntries.AddRange(it.Entries);
                await ApplyRentalDeltaAsync(db, it.Tx, ct); // atomik += (O1) + kira dövizi doğrulama (K2)
            }

            try
            {
                await db.SaveChangesAsync(ct);
                await dbTx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // İşlem anahtarı çakışması (aynı toplu işlem yeniden gönderildi) → TÜM batch geri alınır (idempotent).
                await dbTx.RollbackAsync(ct);
                throw new ValidationException("Bu toplu işlem zaten kaydedilmiş.");
            }
        }, ct);
    }

    public async Task<decimal> GetCariBalanceAsync(Guid cariId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Σ (Borç +AmountInBase, Alacak −AmountInBase). AmountInBase = Amount * Rate.
        var rows = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Cari && e.AccountRef == cariId)
            .Select(e => new { e.Direction, e.Amount })
            .ToListAsync(ct);
        return rows.Sum(r => r.Direction == LedgerDirection.Debit ? r.Amount.AmountInBase : -r.Amount.AmountInBase);
    }

    public async Task<decimal> GetDepozitoBakiyeAsync(Guid cariId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Depozito yükümlülük: Alacak (al) +AmountInBase, Borç (iade/mahsup) −AmountInBase → elde tutulan.
        var rows = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Depozito && e.AccountRef == cariId)
            .Select(e => new { e.Direction, e.Amount })
            .ToListAsync(ct);
        return rows.Sum(r => r.Direction == LedgerDirection.Credit ? r.Amount.AmountInBase : -r.Amount.AmountInBase);
    }

    public async Task<IReadOnlyList<AccountLedgerEntry>> GetCariStatementAsync(Guid cariId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Cari && e.AccountRef == cariId)
            .OrderBy(e => e.EntryDateUtc)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, int>> GetRentalIslemSayilariAsync(
        IReadOnlyCollection<Guid> rentalIds, CancellationToken ct = default)
    {
        if (rentalIds.Count == 0) return [];
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Ters kayıtlar DAHİL sayılır: sayaç monoton artar → ters-kayıt-sonrası yeniden-tahsilat
        // yeni anahtar üretir (Tahsilat-toplamı eski değere dönebilirdi — o yüzden toplam değil SAYI).
        return await db.CashTransactions.AsNoTracking()
            .Where(t => t.RentalId != null && rentalIds.Contains(t.RentalId.Value))
            .GroupBy(t => t.RentalId!.Value)
            .Select(g => new { g.Key, Adet = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Adet, ct);
    }
}
