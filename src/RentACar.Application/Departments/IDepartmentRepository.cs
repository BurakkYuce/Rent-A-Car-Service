using RentACar.Domain.Entities;

namespace RentACar.Application.Departments;

public interface IDepartmentRepository
{
    Task<IReadOnlyList<Department>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Department>> ListActiveAsync(CancellationToken ct = default);
    Task<Department?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(Department department, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Department> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
