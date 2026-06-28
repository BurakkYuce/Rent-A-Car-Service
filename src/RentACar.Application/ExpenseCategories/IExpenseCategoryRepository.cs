using RentACar.Domain.Entities;

namespace RentACar.Application.ExpenseCategories;

public interface IExpenseCategoryRepository
{
    Task<IReadOnlyList<ExpenseCategory>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ExpenseCategory>> ListActiveAsync(CancellationToken ct = default);
    Task<ExpenseCategory?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(ExpenseCategory category, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<ExpenseCategory> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
