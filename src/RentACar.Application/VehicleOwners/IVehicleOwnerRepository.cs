using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleOwners;

public interface IVehicleOwnerRepository
{
    Task<IReadOnlyList<VehicleOwner>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VehicleOwner>> ListActiveAsync(CancellationToken ct = default);
    Task<VehicleOwner?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(VehicleOwner owner, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<VehicleOwner> apply, CancellationToken ct = default);
    /// <summary>F6.1a — satır kilidi + iyimser sürüm karşılaştırması; uyuşmazlık <c>EszamanliDegisiklikException</c>.</summary>
    Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<VehicleOwner> apply, CancellationToken ct = default);
    /// <summary>F6.1a — satır sürümü (Postgres <c>xmin</c>, opak). Yoksa <c>null</c>.</summary>
    Task<string?> VersionAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
