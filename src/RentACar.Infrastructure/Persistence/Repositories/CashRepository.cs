using Microsoft.EntityFrameworkCore;
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
    private static async Task ApplyRentalDeltaAsync(AppDbContext db, CashTransaction tx, CancellationToken ct)
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
                // Yarış: aynı işlem için ikinci ters kayıt (kısmi unique index) → idempotent hata.
                await dbTx.RollbackAsync(ct);
                throw new ValidationException("Bu işlem zaten ters kaydedilmiş.");
            }
        }, ct);
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
}
