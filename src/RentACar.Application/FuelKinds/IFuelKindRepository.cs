using RentACar.Domain.Entities;

namespace RentACar.Application.FuelKinds;

public interface IFuelKindRepository
{
    Task<IReadOnlyList<FuelKind>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FuelKind>> ListActiveAsync(CancellationToken ct = default);
    Task<FuelKind?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(FuelKind kind, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<FuelKind> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
