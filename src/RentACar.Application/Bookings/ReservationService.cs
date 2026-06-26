using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>
/// Rezervasyon iş mantığı + durum makinesi (Rezerv→Onaylı→KirayaCevrildi/İptal).
/// Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class ReservationService(IBookingRepository repository)
{
    private readonly IBookingRepository _repository = repository;

    public Task<IReadOnlyList<Reservation>> ListAsync(CancellationToken ct = default)
        => _repository.ListReservationsAsync(ct);

    public Task<Reservation?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindReservationAsync(id, ct);

    public async Task<Guid> CreateAsync(BookingInput input, CancellationToken ct = default)
    {
        BookingMath.Validate(input);
        var (gun, tutar) = BookingMath.Compute(input);

        // Aktif kira çakışması varsa rezervasyon alınamaz (yumuşak ön-kontrol).
        if (await _repository.HasOverlappingActiveRentalAsync(input.VehicleId, input.BasTar, input.BitTar, null, ct))
            throw new AvailabilityConflictException();

        var reservation = new Reservation
        {
            Durum = ReservationStatus.Rezerv,
            MusteriId = input.MusteriId,
            VehicleId = input.VehicleId,
            BasTar = input.BasTar,
            BitTar = input.BitTar,
            CikisOfisi = input.CikisOfisi,
            DonusOfisi = input.DonusOfisi,
            Gun = gun,
            GunlukUcret = input.GunlukUcret,
            Tutar = tutar,
            Aciklama = input.Aciklama
        };
        await _repository.CreateReservationAsync(reservation, ct);
        return reservation.Id;
    }

    public Task<bool> ConfirmAsync(Guid id, CancellationToken ct = default)
        => Transition(id, ReservationStatus.Onayli, [ReservationStatus.Rezerv], ct);

    public Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
        => Transition(id, ReservationStatus.Iptal, [ReservationStatus.Rezerv, ReservationStatus.Onayli], ct);

    /// <summary>Tasfiye: rezervasyonu kira sözleşmesine çevirir. Yeni kira Id döner.</summary>
    public async Task<Guid> ConvertToRentalAsync(Guid id, CancellationToken ct = default)
    {
        var reservation = await _repository.FindReservationAsync(id, ct)
            ?? throw new ValidationException("Rezervasyon bulunamadı.");
        if (reservation.Durum is not (ReservationStatus.Rezerv or ReservationStatus.Onayli))
            throw new ValidationException("Yalnız Rezerv/Onaylı rezervasyon kiraya çevrilebilir.");

        return await _repository.ConvertToRentalAsync(id, res => new RentalContract
        {
            Durum = RentalStatus.Kirada,
            ReservationId = res.Id,
            MusteriId = res.MusteriId,
            VehicleId = res.VehicleId,
            BasTar = res.BasTar,
            BitTar = res.BitTar,
            CikisOfisi = res.CikisOfisi,
            DonusOfisi = res.DonusOfisi,
            Gun = res.Gun,
            GunlukUcret = res.GunlukUcret,
            Tutar = res.Tutar,
            Tahsilat = 0m,
            Bakiye = res.Tutar,
            Aciklama = res.Aciklama
        }, ct);
    }

    private async Task<bool> Transition(
        Guid id, ReservationStatus to, ReservationStatus[] allowedFrom, CancellationToken ct)
    {
        return await _repository.UpdateReservationAsync(id, r =>
        {
            if (Array.IndexOf(allowedFrom, r.Durum) < 0)
                throw new ValidationException($"Rezervasyon '{r.Durum}' durumundan '{to}' durumuna geçemez.");
            r.Durum = to;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }
}
