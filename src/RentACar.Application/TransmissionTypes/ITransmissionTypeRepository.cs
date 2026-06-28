using RentACar.Domain.Entities;

namespace RentACar.Application.TransmissionTypes;

public interface ITransmissionTypeRepository
{
    Task<IReadOnlyList<TransmissionType>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TransmissionType>> ListActiveAsync(CancellationToken ct = default);
    Task<TransmissionType?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(TransmissionType type, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<TransmissionType> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
