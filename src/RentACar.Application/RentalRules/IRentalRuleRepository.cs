using RentACar.Domain.Entities;

namespace RentACar.Application.RentalRules;

public interface IRentalRuleRepository
{
    Task<IReadOnlyList<RentalRule>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RentalRule>> ListActiveAsync(CancellationToken ct = default);
    Task<RentalRule?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(RentalRule row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<RentalRule> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
