using RentACar.Domain.Entities;

namespace RentACar.Application.FinancialAccounts;

public interface IFinancialAccountRepository
{
    Task<IReadOnlyList<FinancialAccount>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FinancialAccount>> ListActiveAsync(CancellationToken ct = default);
    Task<FinancialAccount?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(FinancialAccount account, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<FinancialAccount> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
