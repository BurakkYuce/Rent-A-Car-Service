using RentACar.Domain.Entities;

namespace RentACar.Application.BrokerYasaklari;

public interface IBrokerBanRepository
{
    Task<IReadOnlyList<BrokerYasak>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BrokerYasak>> ListActiveAsync(CancellationToken ct = default);
    Task<BrokerYasak?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(BrokerYasak row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<BrokerYasak> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
