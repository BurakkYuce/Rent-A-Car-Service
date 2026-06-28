using RentACar.Domain.Entities;

namespace RentACar.Application.Countries;

public interface ICountryRepository
{
    Task<IReadOnlyList<Country>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Country>> ListActiveAsync(CancellationToken ct = default);
    Task<Country?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(Country country, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Country> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
