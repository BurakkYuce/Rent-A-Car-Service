using RentACar.Domain.Entities;

namespace RentACar.Application.Currencies;

public interface ICurrencyRepository
{
    Task<IReadOnlyList<Currency>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Currency>> ListActiveAsync(CancellationToken ct = default);
    Task<Currency?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(Currency currency, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Currency> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
