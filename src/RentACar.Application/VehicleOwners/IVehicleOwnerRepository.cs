using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleOwners;

public interface IVehicleOwnerRepository
{
    Task<IReadOnlyList<VehicleOwner>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VehicleOwner>> ListActiveAsync(CancellationToken ct = default);
    Task<VehicleOwner?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(VehicleOwner owner, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<VehicleOwner> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
