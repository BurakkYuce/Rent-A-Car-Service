using RentACar.Domain.Entities;

namespace RentACar.Application.Crm;

public interface IAnketRepository
{
    Task<IReadOnlyList<Anket>> ListAsync(CancellationToken ct = default);
    Task<Anket?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(Anket row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Anket> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface ISikayetRepository
{
    Task<IReadOnlyList<Sikayet>> ListAsync(CancellationToken ct = default);
    Task<Sikayet?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(Sikayet row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Sikayet> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
