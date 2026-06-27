using RentACar.Domain.Entities;

namespace RentACar.Application.Expenses;

public interface IExpenseRepository
{
    Task<IReadOnlyList<Expense>> ListAsync(CancellationToken ct = default);
    Task<Expense?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gider belgesi + DENGELİ defter kümesini TEK transaction'da işler. No boşluksuz tahsis
    /// edilir; gider/defter DB-seviyesinde değişmez (trigger).
    /// </summary>
    Task PostAsync(Expense expense, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);
}
