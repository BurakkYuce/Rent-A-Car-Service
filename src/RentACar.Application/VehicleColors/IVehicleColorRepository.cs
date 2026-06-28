using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleColors;

public interface IVehicleColorRepository
{
    Task<IReadOnlyList<VehicleColor>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VehicleColor>> ListActiveAsync(CancellationToken ct = default);
    Task<VehicleColor?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(VehicleColor color, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<VehicleColor> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
