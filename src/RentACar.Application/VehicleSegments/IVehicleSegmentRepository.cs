using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleSegments;

public interface IVehicleSegmentRepository
{
    Task<IReadOnlyList<VehicleSegment>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VehicleSegment>> ListActiveAsync(CancellationToken ct = default);
    Task<VehicleSegment?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(VehicleSegment segment, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<VehicleSegment> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
