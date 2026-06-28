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

    public async Task PostAsync(
        CashTransaction tx, IReadOnlyList<AccountLedgerEntry> entries,
        decimal rentalTahsilatDelta, CancellationToken ct = default)
    {
        // Dengelilik guard: Σ Borç(base) == Σ Alacak(base).
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Defter dengesiz: borç {debit} ≠ alacak {credit}.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var dbTx = await db.Database.BeginTransactionAsync(ct);

        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "CashNo", ct);
        tx.No = $"{(tx.Tip == CashTransactionType.Odeme ? "TD" : "TH")}-{n:D6}";
        db.CashTransactions.Add(tx);
        db.AccountLedgerEntries.AddRange(entries);

        if (tx.RentalId is Guid rentalId)
        {
            var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == rentalId, ct);
            if (rental is not null)
            {
                rental.Tahsilat += rentalTahsilatDelta;
                rental.Bakiye = rental.GenelToplam - rental.Tahsilat;
                rental.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

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

        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var dbTx = await db.Database.BeginTransactionAsync(ct);

        // ATOMİK: tüm satırlar TEK transaction'da. No'lar boşluksuz; rollback olursa sıra geri alınır.
        foreach (var it in items)
        {
            var n = await SequenceAllocator.NextAsync(db, db.TenantId, "CashNo", ct);
            it.Tx.No = $"{(it.Tx.Tip == CashTransactionType.Odeme ? "TD" : "TH")}-{n:D6}";
            db.CashTransactions.Add(it.Tx);
            db.AccountLedgerEntries.AddRange(it.Entries);

            if (it.Tx.RentalId is Guid rentalId)
            {
                var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == rentalId, ct);
                if (rental is not null)
                {
                    rental.Tahsilat += it.RentalTahsilatDelta;
                    rental.Bakiye = rental.GenelToplam - rental.Tahsilat;
                    rental.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
            }
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

    public async Task<IReadOnlyList<AccountLedgerEntry>> GetCariStatementAsync(Guid cariId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Cari && e.AccountRef == cariId)
            .OrderBy(e => e.EntryDateUtc)
            .ToListAsync(ct);
    }
}
