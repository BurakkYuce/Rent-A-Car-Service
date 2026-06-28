using RentACar.Domain.Entities;

namespace RentACar.Application.Brands;

public interface IBrandRepository
{
    Task<IReadOnlyList<Brand>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Brand>> ListActiveAsync(CancellationToken ct = default);
    Task<Brand?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(Brand brand, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Brand> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
