using RentACar.Domain.Entities;

namespace RentACar.Application.RezSartlar;

public interface IRezSartRepository
{
    /// <summary>Filtreli liste. <paramref name="filter"/> null → tüm kayıtlar.</summary>
    Task<IReadOnlyList<RezSart>> ListAsync(RezSartFilter? filter = null, CancellationToken ct = default);
    Task<RezSart?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(RezSart row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<RezSart> apply, CancellationToken ct = default);
    /// <summary>F5.1 — satır kilidi + iyimser sürüm karşılaştırması (bkz. <c>IBookingRepository.UpdateReservationAsync</c>).</summary>
    Task<bool> UpdateAsync(Guid id, string? beklenenSurum, Action<RezSart> apply, CancellationToken ct = default);
    /// <summary>F5.1 — satır sürümü (Postgres <c>xmin</c>, opak). Yoksa <c>null</c>.</summary>
    Task<string?> SurumAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
