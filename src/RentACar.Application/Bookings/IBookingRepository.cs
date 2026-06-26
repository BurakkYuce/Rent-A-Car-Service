using RentACar.Domain.Entities;

namespace RentACar.Application.Bookings;

/// <summary>
/// Rezervasyon + kira kalıcılığı. Boşluksuz sıra tahsisi ve double-booking koruması
/// (exclusion constraint) Infrastructure'da transaction içinde uygulanır.
/// </summary>
public interface IBookingRepository
{
    // Rezervasyon
    Task<IReadOnlyList<Reservation>> ListReservationsAsync(CancellationToken ct = default);
    Task<Reservation?> FindReservationAsync(Guid id, CancellationToken ct = default);
    /// <summary>ReservationNo'yu boşluksuz tahsis edip ekler (transaction).</summary>
    Task CreateReservationAsync(Reservation reservation, CancellationToken ct = default);
    Task<bool> UpdateReservationAsync(Guid id, Action<Reservation> apply, CancellationToken ct = default);

    // Kira
    Task<IReadOnlyList<RentalContract>> ListRentalsAsync(CancellationToken ct = default);
    Task<RentalContract?> FindRentalAsync(Guid id, CancellationToken ct = default);
    /// <summary>SozlesmeNo'yu boşluksuz tahsis edip ekler; çakışmada AvailabilityConflictException.</summary>
    Task CreateRentalAsync(RentalContract contract, CancellationToken ct = default);
    Task<bool> UpdateRentalAsync(Guid id, Action<RentalContract> apply, CancellationToken ct = default);

    /// <summary>Verilen araç+aralık için aktif (Kirada) kira çakışması var mı?</summary>
    Task<bool> HasOverlappingActiveRentalAsync(
        Guid vehicleId, DateTimeOffset basTar, DateTimeOffset bitTar,
        Guid? excludeRentalId = null, CancellationToken ct = default);

    /// <summary>
    /// Tasfiye: rezervasyondan kira sözleşmesi üretir (TEK transaction): rental ekle
    /// (no tahsis + exclusion), rezervasyonu KirayaCevrildi + link. Yeni kira Id döner.
    /// </summary>
    Task<Guid> ConvertToRentalAsync(
        Guid reservationId, Func<Reservation, RentalContract> buildRental, CancellationToken ct = default);
}
