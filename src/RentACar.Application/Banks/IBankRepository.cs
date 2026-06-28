using RentACar.Domain.Entities;

namespace RentACar.Application.Banks;

public interface IBankRepository
{
    Task<IReadOnlyList<Bank>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Bank>> ListActiveAsync(CancellationToken ct = default);
    Task<Bank?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(Bank bank, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Bank> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
