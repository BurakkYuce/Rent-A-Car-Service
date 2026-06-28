using RentACar.Domain.Entities;

namespace RentACar.Application.ReservationSources;

public interface IReservationSourceRepository
{
    Task<IReadOnlyList<ReservationSource>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReservationSource>> ListActiveAsync(CancellationToken ct = default);
    Task<ReservationSource?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(ReservationSource source, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<ReservationSource> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
