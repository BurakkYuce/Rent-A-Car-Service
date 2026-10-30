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
    Task<IReadOnlyList<Reservation>> ListReservationsAsync(Authorization.BranchScope.BranchFilter scope = default, CancellationToken ct = default);
    /// <summary>Rezervasyon listesi: filtre + müşteri/araç birleşimi (FAZ-48; SearchRentalRowsAsync deseni).
    /// Ayrı bir IReservationRepository AÇILMAZ — rezervasyon kalıcılığı bu arayüzde.</summary>
    Task<IReadOnlyList<ReservationRow>> SearchReservationsAsync(ReservationFilter filter, CancellationToken ct = default);
    Task<Reservation?> FindReservationAsync(Guid id, CancellationToken ct = default);
    /// <summary>ReservationNo'yu boşluksuz tahsis edip ekler (transaction).</summary>
    Task CreateReservationAsync(Reservation reservation, CancellationToken ct = default);
    Task<bool> UpdateReservationAsync(Guid id, Action<Reservation> apply, CancellationToken ct = default);
    /// <summary>
    /// F5.1 — yukarıdakiyle aynı; ek olarak satır <c>FOR UPDATE</c> ile kilitlenir ve <paramref name="expectedVersion"/>
    /// doluysa kilit ALTINDA okunan sürümle (<see cref="ReservationVersionAsync"/>) karşılaştırılır; farklıysa
    /// <see cref="Common.ConcurrentModificationException"/> — hiçbir şey yazılmaz.
    /// </summary>
    Task<bool> UpdateReservationAsync(Guid id, string? expectedVersion, Action<Reservation> apply, CancellationToken ct = default);
    /// <summary>F5.1 — rezervasyonun satır sürümü (Postgres <c>xmin</c>, opak). Yoksa / kapsamda değilse <c>null</c>.</summary>
    Task<string?> ReservationVersionAsync(Guid id, CancellationToken ct = default);

    // Kira
    /// <summary><paramref name="sube"/> verilirse yalnız o çıkış ofisi (rol bazlı şube kapsamı).</summary>
    Task<IReadOnlyList<RentalContract>> ListRentalsAsync(Authorization.BranchScope.BranchFilter scope = default, CancellationToken ct = default);
    /// <summary>Kira listesi: filtre + müşteri/araç/fatura-durumu birleşimi (salt-okunur projeksiyon).</summary>
    Task<IReadOnlyList<RentalRow>> SearchRentalRowsAsync(RentalFilter filter, CancellationToken ct = default);
    Task<RentalContract?> FindRentalAsync(Guid id, CancellationToken ct = default);
    /// <summary>SozlesmeNo'yu boşluksuz tahsis edip kirayı + <paramref name="lines"/> (sistem ücret satırları, dönem
    /// planı) TEK transaction'da ekler; GenelToplam/Bakiye kalemlerle birlikte yazılır. Herhangi bir parça
    /// patlarsa hiçbiri kalmaz. Çakışmada AvailabilityConflictException.</summary>
    Task CreateRentalAsync(RentalContract contract, NewRentalLines lines, CancellationToken ct = default);
    /// <summary>F4.1 adversarial M2: satır <c>FOR UPDATE</c> ile kilitlenip kilit ALTINDA okunur (aynı TX).</summary>
    Task<bool> UpdateRentalAsync(Guid id, Action<RentalContract> apply, CancellationToken ct = default);
    /// <summary>
    /// F4.3 adversarial F2: yukarıdakiyle aynı; ek olarak <paramref name="expectedVersion"/> doluysa satır kilidi
    /// ALTINDA okunan güncel sürümle (<see cref="RentalVersionAsync"/>) karşılaştırılır, farklıysa
    /// <see cref="Common.ConcurrentModificationException"/> — hiçbir şey yazılmaz (TOCTOU yok).
    /// </summary>
    Task<bool> UpdateRentalAsync(Guid id, string? expectedVersion, Action<RentalContract> apply, CancellationToken ct = default);
    /// <summary>
    /// Kiranın satır sürümü (Postgres <c>xmin</c>, opak metin): satıra yapılan HER güncellemede değişir (EF, ham SQL,
    /// toplam senkronu — hangi yoldan olursa olsun). Kira yoksa / kapsamda değilse (RLS) <c>null</c>.
    /// </summary>
    Task<string?> RentalVersionAsync(Guid id, CancellationToken ct = default);
    /// <summary>Kira + aracı AYNI transaction'da günceller (teslim/dönüş km → araç odometresi;
    /// ServiceRecordRepository.TransitionAsync deseni). Araç TX İÇİNDE okunur (PgRetry'de bayat okuma olmaz).
    /// kmLog (FAZ 2.5): verilirse dönüş odometresi km zaman-serisine AYNI transaction'da yazılır
    /// (delege db'yi açmadığından log insert'i repo metodunun kendisinde).
    /// kabis: verilirse KABİS kuyruk kaydı AYNI transaction'da yazılır (outbox; aynı kira × tür varsa no-op).</summary>
    Task<bool> UpdateRentalWithVehicleAsync(
        Guid id, Action<RentalContract> applyRental, Action<Vehicle> applyVehicle,
        Func<RentalContract, VehicleKmLog>? kmLog = null, RentACar.Application.Kabis.KabisOutbox? kabis = null,
        CancellationToken ct = default);

    /// <summary>
    /// F4.1 adversarial M2/M3: yukarıdakiyle aynı, ek olarak kira-fatura kilidi (fatura kesimiyle serileşir)
    /// ve kilit ALTINDA okunan <see cref="KiraKilitBaglami"/> (ek hizmet toplamı, açık fatura). Toplamı
    /// ek hizmete bağlı (dönüş) ya da faturaya bağlı (iptal) geçişler bunu kullanır.
    /// </summary>
    Task<bool> UpdateRentalWithVehicleAsync(
        Guid id, Action<RentalContract, KiraKilitBaglami> applyRental, Action<Vehicle> applyVehicle,
        Func<RentalContract, VehicleKmLog>? kmLog = null, RentACar.Application.Kabis.KabisOutbox? kabis = null,
        CancellationToken ct = default);

    /// <summary>Verilen araç+aralık için aktif (Kirada) kira çakışması var mı?</summary>
    Task<bool> HasOverlappingActiveRentalAsync(
        Guid vehicleId, DateTimeOffset startDate, DateTimeOffset bitTar,
        Guid? excludeRentalId = null, CancellationToken ct = default);

    /// <summary>
    /// Tasfiye: rezervasyondan kira sözleşmesi üretir (TEK transaction): rental ekle
    /// (no tahsis + exclusion) + <paramref name="buildLines"/> (kilit ALTINDA kurulan kiradan; ücret satırları + dönem
    /// planı — SAF olmalı: kilit altında DB'ye gitmez, #416 L1), rezervasyonu KirayaCevrildi + link. Yeni kira Id döner.
    /// </summary>
    Task<Guid> ConvertToRentalAsync(
        Guid reservationId, Func<Reservation, RentalContract> buildRental,
        Func<RentalContract, NewRentalLines> buildLines, CancellationToken ct = default);
}

/// <summary>Yeni kirayla AYNI transaction'da yazılan alt satırlar (DEVIR §6: kira oluşturma atomik).</summary>
public sealed record NewRentalLines(IReadOnlyList<RentalAddOn> AddOns, IReadOnlyList<FaturaDonemi> Periods);

/// <summary>Kira satır + fatura kilidi ALTINDA okunmuş bağlam (F4.1 adversarial M2/M3).</summary>
/// <param name="EkHizmetToplam">Kiranın ek hizmet kalemlerinin brüt toplamı (commit edilmiş son hâl).</param>
/// <param name="AcikFaturaVar">İade edilmemiş base/fark/dönem faturası var mı.</param>
/// <param name="SonYansitilanHgs">Bu kiraya müşteriye YANSITILMIŞ en geç HGS geçişinin anı (yoksa null). Kira satırı
/// FOR UPDATE kilitliyken okunur; HGS yansıtması kirayı FOR SHARE kilitlediği için ikisi serileşir (#391 M3).</param>
public sealed record KiraKilitBaglami(decimal EkHizmetToplam, bool AcikFaturaVar, DateTimeOffset? SonYansitilanHgs = null);
