using RentACar.Domain.Entities;

namespace RentACar.Application.CancelReasons;

public interface ICancelReasonRepository
{
    Task<IReadOnlyList<CancelReason>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CancelReason>> ListActiveAsync(CancellationToken ct = default);
    Task<CancelReason?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(CancelReason reason, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<CancelReason> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
