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

    public async Task<IReadOnlyList<Expense>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Expenses.AsNoTracking().OrderByDescending(x => x.Tarih).ToListAsync(ct);
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

        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "ExpenseNo", ct);
        expense.No = $"GD-{n:D6}";

        db.Expenses.Add(expense);
        db.AccountLedgerEntries.AddRange(entries);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
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
    }
}
