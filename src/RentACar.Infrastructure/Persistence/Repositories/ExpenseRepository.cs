using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Gider kalıcılığı. PostAsync: No tahsisi + gider + DENGELİ defter kümesi → TEK transaction.
/// Gider/defter immutable (DB trigger).
/// </summary>
public sealed class ExpenseRepository(IDbContextFactory<AppDbContext> factory) : IExpenseRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Expense>> ListAsync(
        RentACar.Application.Authorization.BranchScope.BranchFilter kapsam,
        RentACar.Application.Expenses.ExpenseFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Expenses.AsNoTracking();
        // C3 ŞABLON (BranchScope.InScope ile birebir): FK-eşit VEYA metin-eşit (Ordinal).
        // ÖNCE kapsam, SONRA kullanıcı filtresi — filtre kapsamı genişletemez.
        if (!kapsam.Unrestricted)
        {
            var kid = kapsam.SubeId; var kad = kapsam.SubeAd;
            q = q.Where(x => (kid != null && x.SubeId == kid)
                          || ((kid == null || x.SubeId == null) && kad != null && x.Sube != null && x.Sube.Trim() == kad)); // C5
        }

        if (filter is not null)
        {
            if (!string.IsNullOrWhiteSpace(filter.Ara))
            {
                var t = filter.Ara.Trim();
                q = q.Where(x => EF.Functions.ILike(x.No, $"%{t}%")
                              || (x.EvrakNo != null && EF.Functions.ILike(x.EvrakNo, $"%{t}%"))
                              || (x.Aciklama != null && EF.Functions.ILike(x.Aciklama, $"%{t}%")));
            }
            if (filter.CariId is { } cid) q = q.Where(x => x.CariId == cid);
            if (filter.Tip is { } tip) q = q.Where(x => x.Tip == tip);
            if (!string.IsNullOrWhiteSpace(filter.Sube))
            {
                var s = filter.Sube.Trim();
                q = q.Where(x => x.Sube != null && x.Sube.Trim() == s);
            }
            if (filter.Bas is { } b) q = q.Where(x => x.Tarih >= b);
            if (filter.Bit is { } t2) q = q.Where(x => x.Tarih <= t2);
            if (!string.IsNullOrWhiteSpace(filter.Plaka))
            {
                // Plaka Expense'te YOK → araç tablosundan alt-sorgu (tenant filtresi orada da geçerli).
                // Plakalar DB'de normalize saklanır (büyük harf, boşluksuz: "34AA01"); kullanıcı ise
                // "34 AA 01" yazar. Arama terimi AYNI normalizasyondan geçmezse hiçbir şey bulunmaz.
                var p = filter.Plaka.Trim().ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(x => x.VehicleId != null && db.Vehicles
                    .Where(v => EF.Functions.ILike(v.Plaka, $"%{p}%"))
                    .Select(v => (Guid?)v.Id).Contains(x.VehicleId));
            }
        }

        return await q.OrderByDescending(x => x.Tarih).ToListAsync(ct);
    }

    public async Task<Expense?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Expenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task PostAsync(Expense expense, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Gider defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var n = await SequenceAllocator.NextAsync(db, db.TenantId, "ExpenseNo", ct);
            expense.No = $"GD-{n:D6}";

            db.Expenses.Add(expense);
            db.AccountLedgerEntries.AddRange(entries);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    public async Task PostBatchAsync(IReadOnlyList<ExpensePosting> items, CancellationToken ct = default)
    {
        if (items.Count == 0) throw new ValidationException("Toplu gider en az bir kalem içermelidir.");

        foreach (var it in items)
        {
            var d = it.Entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
            var c = it.Entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
            if (d != c) throw new ValidationException($"Gider defteri dengesiz: borç {d} ≠ alacak {c}.");
        }

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // ATOMİK: tüm kalemler TEK transaction'da. No'lar boşluksuz; rollback olursa sıra geri alınır.
            foreach (var it in items)
            {
                var n = await SequenceAllocator.NextAsync(db, db.TenantId, "ExpenseNo", ct);
                it.Expense.No = $"GD-{n:D6}";
                db.Expenses.Add(it.Expense);
                db.AccountLedgerEntries.AddRange(it.Entries);
            }

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await tx.RollbackAsync(ct);
                throw new ValidationException("Bu toplu gider zaten kaydedilmiş.");
            }
        }, ct);
    }
}
