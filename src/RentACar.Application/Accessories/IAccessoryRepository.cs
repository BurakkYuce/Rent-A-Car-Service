using RentACar.Domain.Entities;

namespace RentACar.Application.Accessories;

public interface IAccessoryRepository
{
    Task<IReadOnlyList<Accessory>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Accessory>> ListActiveAsync(CancellationToken ct = default);
    Task<Accessory?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(Accessory accessory, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Accessory> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
