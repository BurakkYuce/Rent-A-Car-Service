using RentACar.Domain.Entities;

namespace RentACar.Application.CustomerGroups;

public interface ICustomerGroupRepository
{
    Task<IReadOnlyList<CustomerGroup>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CustomerGroup>> ListActiveAsync(CancellationToken ct = default);
    Task<CustomerGroup?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(CustomerGroup group, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<CustomerGroup> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
