using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleTypes;

public interface IVehicleTypeRepository
{
    Task<IReadOnlyList<VehicleType>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VehicleType>> ListActiveAsync(CancellationToken ct = default);
    Task<VehicleType?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(VehicleType type, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<VehicleType> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
