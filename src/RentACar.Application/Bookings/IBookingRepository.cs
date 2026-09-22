using RentACar.Domain.Entities;

namespace RentACar.Application.Bookings;

/// <summary>
/// Rezervasyon + kira kalıcılığı. Boşluksuz sıra tahsisi ve double-booking koruması
/// (exclusion constraint) Infrastructure'da transaction içinde uygulanır.
/// </summary>
public interface IBookingRepository
{
    // Rezervasyon
    /// <summary><paramref name="sube"/> verilirse yalnız o çıkış ofisi (rol bazlı şube kapsamı).</summary>
    Task<IReadOnlyList<Reservation>> ListReservationsAsync(Authorization.BranchScope.BranchFilter kapsam = default, CancellationToken ct = default);
    /// <summary>Rezervasyon listesi: filtre + müşteri/araç birleşimi (FAZ-48; SearchRentalRowsAsync deseni).
    /// Ayrı bir IReservationRepository AÇILMAZ — rezervasyon kalıcılığı bu arayüzde.</summary>
    Task<IReadOnlyList<ReservationRow>> SearchReservationsAsync(ReservationFilter filter, CancellationToken ct = default);
    Task<Reservation?> FindReservationAsync(Guid id, CancellationToken ct = default);
    /// <summary>ReservationNo'yu boşluksuz tahsis edip ekler (transaction).</summary>
    Task CreateReservationAsync(Reservation reservation, CancellationToken ct = default);
    Task<bool> UpdateReservationAsync(Guid id, Action<Reservation> apply, CancellationToken ct = default);

    // Kira
    /// <summary><paramref name="sube"/> verilirse yalnız o çıkış ofisi (rol bazlı şube kapsamı).</summary>
    Task<IReadOnlyList<RentalContract>> ListRentalsAsync(Authorization.BranchScope.BranchFilter kapsam = default, CancellationToken ct = default);
    /// <summary>Kira listesi: filtre + müşteri/araç/fatura-durumu birleşimi (salt-okunur projeksiyon).</summary>
    Task<IReadOnlyList<RentalRow>> SearchRentalRowsAsync(RentalFilter filter, CancellationToken ct = default);
    Task<RentalContract?> FindRentalAsync(Guid id, CancellationToken ct = default);
    /// <summary>SozlesmeNo'yu boşluksuz tahsis edip ekler; çakışmada AvailabilityConflictException.</summary>
    Task CreateRentalAsync(RentalContract contract, CancellationToken ct = default);
    /// <summary>F4.1 adversarial M2: satır <c>FOR UPDATE</c> ile kilitlenip kilit ALTINDA okunur (aynı TX).</summary>
    Task<bool> UpdateRentalAsync(Guid id, Action<RentalContract> apply, CancellationToken ct = default);
    /// <summary>Kira + aracı AYNI transaction'da günceller (teslim/dönüş km → araç odometresi;
    /// ServiceRecordRepository.TransitionAsync deseni). Araç TX İÇİNDE okunur (PgRetry'de bayat okuma olmaz).
    /// kmLog (FAZ 2.5): verilirse dönüş odometresi km zaman-serisine AYNI transaction'da yazılır
    /// (delege db'yi açmadığından log insert'i repo metodunun kendisinde).</summary>
    Task<bool> UpdateRentalWithVehicleAsync(
        Guid id, Action<RentalContract> applyRental, Action<Vehicle> applyVehicle,
        Func<RentalContract, VehicleKmLog>? kmLog = null, CancellationToken ct = default);

    /// <summary>
    /// F4.1 adversarial M2/M3: yukarıdakiyle aynı, ek olarak kira-fatura kilidi (fatura kesimiyle serileşir)
    /// ve kilit ALTINDA okunan <see cref="KiraKilitBaglami"/> (ek hizmet toplamı, açık fatura). Toplamı
    /// ek hizmete bağlı (dönüş) ya da faturaya bağlı (iptal) geçişler bunu kullanır.
    /// </summary>
    Task<bool> UpdateRentalWithVehicleAsync(
        Guid id, Action<RentalContract, KiraKilitBaglami> applyRental, Action<Vehicle> applyVehicle,
        Func<RentalContract, VehicleKmLog>? kmLog = null, CancellationToken ct = default);

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

/// <summary>Kira satır + fatura kilidi ALTINDA okunmuş bağlam (F4.1 adversarial M2/M3).</summary>
/// <param name="EkHizmetToplam">Kiranın ek hizmet kalemlerinin brüt toplamı (commit edilmiş son hâl).</param>
/// <param name="AcikFaturaVar">İade edilmemiş base/fark/dönem faturası var mı.</param>
public sealed record KiraKilitBaglami(decimal EkHizmetToplam, bool AcikFaturaVar);
