using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>
/// Rezervasyon iş mantığı + durum makinesi (Rezerv→Onaylı→KirayaCevrildi/İptal).
/// Tenant izolasyonu/audit alt katmanda otomatik. Liste rol bazlı şube kapsamıyla (çıkış ofisi).
/// </summary>
public sealed class ReservationService(IBookingRepository repository, ICurrentUser currentUser, PricingService pricing)
{
    private readonly IBookingRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly PricingService _pricing = pricing;

    public Task<IReadOnlyList<Reservation>> ListAsync(CancellationToken ct = default)
        => _repository.ListReservationsAsync(BranchScope.Effective(_currentUser), ct);

    public Task<Reservation?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindReservationAsync(id, ct);

    public async Task<Guid> CreateAsync(BookingInput input, CancellationToken ct = default)
    {
        BookingMath.Validate(input);
        var (gun, tutar) = await _pricing.PriceAsync(input, ct); // fiyat motoru: manuel >0 kazanır, yoksa tarife

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
            KmLimit = input.KmLimit,
            FazlaKmUcret = input.FazlaKmUcret,
            YakitBirimUcret = input.YakitBirimUcret,
            Provizyon = input.Provizyon,
            Depozito = input.Depozito,
            KomisyonOran = input.KomisyonOran,
            KomisyonTutar = input.KomisyonTutar,
            DropUcreti = input.DropUcreti,
            SonraOdeOran = input.SonraOdeOran,
            Aciklama = input.Aciklama,
            Kaynak = string.IsNullOrWhiteSpace(input.Kaynak) ? null : input.Kaynak.Trim()
        };
        await _repository.CreateReservationAsync(reservation, ct);
        return reservation.Id;
    }

    /// <summary>
    /// Rezervasyon düzenleme (roadmap I2): yalnız Rezerv/Onaylı durumda — tarih/araç/fiyat/ek alanlar/kaynak
    /// güncellenir, fiyat yeniden hesaplanır, aktif kira çakışması yeniden kontrol edilir. Defter etkilemez.
    /// </summary>
    public async Task<bool> UpdateAsync(Guid id, BookingInput input, CancellationToken ct = default)
    {
        BookingMath.Validate(input);
        var existing = await _repository.FindReservationAsync(id, ct);
        if (existing is null) return false;
        if (existing.Durum is not (ReservationStatus.Rezerv or ReservationStatus.Onayli))
            throw new ValidationException("Yalnız Rezerv/Onaylı rezervasyon düzenlenebilir.");

        var (gun, tutar) = await _pricing.PriceAsync(input, ct);

        if (await _repository.HasOverlappingActiveRentalAsync(input.VehicleId, input.BasTar, input.BitTar, null, ct))
            throw new AvailabilityConflictException();

        return await _repository.UpdateReservationAsync(id, r =>
        {
            if (r.Durum is not (ReservationStatus.Rezerv or ReservationStatus.Onayli))
                throw new ValidationException("Yalnız Rezerv/Onaylı rezervasyon düzenlenebilir.");
            r.MusteriId = input.MusteriId;
            r.VehicleId = input.VehicleId;
            r.BasTar = input.BasTar;
            r.BitTar = input.BitTar;
            r.CikisOfisi = input.CikisOfisi;
            r.DonusOfisi = input.DonusOfisi;
            r.Gun = gun;
            r.GunlukUcret = input.GunlukUcret;
            r.Tutar = tutar;
            r.KmLimit = input.KmLimit;
            r.FazlaKmUcret = input.FazlaKmUcret;
            r.YakitBirimUcret = input.YakitBirimUcret;
            r.Provizyon = input.Provizyon;
            r.Depozito = input.Depozito;
            r.KomisyonOran = input.KomisyonOran;
            r.KomisyonTutar = input.KomisyonTutar;
            r.DropUcreti = input.DropUcreti;
            r.SonraOdeOran = input.SonraOdeOran;
            r.Aciklama = input.Aciklama;
            r.Kaynak = string.IsNullOrWhiteSpace(input.Kaynak) ? null : input.Kaynak.Trim();
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
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
            KmLimit = res.KmLimit,
            FazlaKmUcret = res.FazlaKmUcret,
            YakitBirimUcret = res.YakitBirimUcret,
            Tutar = res.Tutar,
            GenelToplam = res.Tutar,
            Tahsilat = 0m,
            Bakiye = res.Tutar,
            Provizyon = res.Provizyon,
            Depozito = res.Depozito,
            KomisyonOran = res.KomisyonOran,
            KomisyonTutar = res.KomisyonTutar,
            DropUcreti = res.DropUcreti,
            SonraOdeOran = res.SonraOdeOran,
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
