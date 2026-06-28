using RentACar.Domain.Entities;

namespace RentACar.Application.Expenses;

/// <summary>Toplu giderde tek satır: gider belgesi + dengeli defter kümesi.</summary>
public sealed record ExpensePosting(Expense Expense, IReadOnlyList<AccountLedgerEntry> Entries);

public interface IExpenseRepository
{
    Task<IReadOnlyList<Expense>> ListAsync(CancellationToken ct = default);
    Task<Expense?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gider belgesi + DENGELİ defter kümesini TEK transaction'da işler. No boşluksuz tahsis
    /// edilir; gider/defter DB-seviyesinde değişmez (trigger).
    /// </summary>
    Task PostAsync(Expense expense, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);

    /// <summary>
    /// Toplu gider: çok kalem dengeli kayıt + No tahsisi TEK transaction'da (ATOMİK hep-ya-hiç).
    /// Bir kalem geçersiz/çakışırsa hiçbiri yazılmaz; No boşluğu oluşmaz. IslemAnahtari kısmi unique
    /// index → aynı toplu giderin çift-submit'i UniqueViolation ile tüm batch'i geri alır (idempotent).
    /// </summary>
    Task PostBatchAsync(IReadOnlyList<ExpensePosting> items, CancellationToken ct = default);
}
