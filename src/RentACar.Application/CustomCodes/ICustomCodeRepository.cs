using RentACar.Domain.Entities;

namespace RentACar.Application.CustomCodes;

public interface ICustomCodeRepository
{
    Task<IReadOnlyList<CustomCode>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CustomCode>> ListActiveAsync(CancellationToken ct = default);
    Task<CustomCode?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(CustomCode code, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<CustomCode> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
