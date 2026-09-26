using RentACar.Domain.Entities;

namespace RentACar.Application.FinancialAccounts;

public interface IFinancialAccountRepository : Common.IVersionedRepository<FinancialAccount>
{
    Task<IReadOnlyList<FinancialAccount>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FinancialAccount>> ListActiveAsync(CancellationToken ct = default);
    Task<FinancialAccount?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(FinancialAccount account, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<FinancialAccount> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>FAZ-50 adversarial M2 — bu hesabın defterde hareketi var mı (AccountRef eşleşmesi).</summary>
    Task<bool> HasLedgerHistoryAsync(Guid id, CancellationToken ct = default);
}
