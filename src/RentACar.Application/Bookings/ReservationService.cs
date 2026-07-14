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

    public async Task<Reservation?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _repository.FindReservationAsync(id, ct);
        if (r is not null) BranchScope.RequireInScope(_currentUser, r.CikisOfisi); // adversarial M3
        return r;
    }

    public async Task<Guid> CreateAsync(BookingInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        BookingMath.Validate(input);
        TarihPolitikasi.RezervasyonBaslangic(input.BasTar); // geçmişe kapalı; gelecek ≤ +1yıl
        var pr = await _pricing.PriceAsync(input, ct); // fiyat motoru: manuel >0 kazanır, yoksa tarife

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
            Gun = pr.Gun,
            GunlukUcret = input.GunlukUcret,
            Tutar = pr.Tutar,
            HediyeGun = pr.HediyeGun, FaturalananGun = pr.FaturalananGun, IskontoTutar = pr.IskontoTutar, HaftaSonuFark = pr.HaftaSonuFark,
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
            Kaynak = string.IsNullOrWhiteSpace(input.Kaynak) ? null : input.Kaynak.Trim(),
            KampanyaKodu = string.IsNullOrWhiteSpace(input.KampanyaKodu) ? null : input.KampanyaKodu.Trim()
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
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        BookingMath.Validate(input);
        var existing = await _repository.FindReservationAsync(id, ct);
        if (existing is null) return false;
        if (existing.Durum is not (ReservationStatus.Rezerv or ReservationStatus.Onayli))
            throw new ValidationException("Yalnız Rezerv/Onaylı rezervasyon düzenlenebilir.");
        // Tarih politikası YALNIZ başlangıç GERÇEKTEN değişiyorsa (typo koruması) — yaşlanmış rezervasyonun
        // (başlangıcı doğal olarak geçmişte kalmış, hâlâ Rezerv/Onaylı) not/araç/fiyat düzenlemesini KİLİTLEME
        // (adversarial H5). Yeni bir geçmiş/aşırı-ileri tarihe taşıma hâlâ reddedilir.
        if (input.BasTar != existing.BasTar)
            TarihPolitikasi.RezervasyonBaslangic(input.BasTar);

        var pr = await _pricing.PriceAsync(input, ct);

        if (await _repository.HasOverlappingActiveRentalAsync(input.VehicleId, input.BasTar, input.BitTar, null, ct))
            throw new AvailabilityConflictException();

        return await _repository.UpdateReservationAsync(id, r =>
        {
            BranchScope.RequireInScope(_currentUser, r.CikisOfisi); // adversarial M3
            if (r.Durum is not (ReservationStatus.Rezerv or ReservationStatus.Onayli))
                throw new ValidationException("Yalnız Rezerv/Onaylı rezervasyon düzenlenebilir.");
            r.MusteriId = input.MusteriId;
            r.VehicleId = input.VehicleId;
            r.BasTar = input.BasTar;
            r.BitTar = input.BitTar;
            r.CikisOfisi = input.CikisOfisi;
            r.DonusOfisi = input.DonusOfisi;
            r.Gun = pr.Gun;
            r.GunlukUcret = input.GunlukUcret;
            r.Tutar = pr.Tutar;
            r.HediyeGun = pr.HediyeGun; r.FaturalananGun = pr.FaturalananGun; r.IskontoTutar = pr.IskontoTutar; r.HaftaSonuFark = pr.HaftaSonuFark;
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
            r.KampanyaKodu = string.IsNullOrWhiteSpace(input.KampanyaKodu) ? null : input.KampanyaKodu.Trim();
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> ConfirmAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        return Transition(id, ReservationStatus.Onayli, [ReservationStatus.Rezerv], ct);
    }

    public Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        return Transition(id, ReservationStatus.Iptal, [ReservationStatus.Rezerv, ReservationStatus.Onayli], ct);
    }

    /// <summary>Tasfiye: rezervasyonu kira sözleşmesine çevirir. Yeni kira Id döner.</summary>
    public async Task<Guid> ConvertToRentalAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        var reservation = await _repository.FindReservationAsync(id, ct)
            ?? throw new ValidationException("Rezervasyon bulunamadı.");
        BranchScope.RequireInScope(_currentUser, reservation.CikisOfisi); // adversarial M3
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
            HediyeGun = res.HediyeGun, FaturalananGun = res.FaturalananGun, IskontoTutar = res.IskontoTutar, HaftaSonuFark = res.HaftaSonuFark,
            GenelToplam = res.Tutar,
            Tahsilat = 0m,
            Bakiye = res.Tutar,
            Provizyon = res.Provizyon,
            Depozito = res.Depozito,
            KomisyonOran = res.KomisyonOran,
            KomisyonTutar = res.KomisyonTutar,
            DropUcreti = res.DropUcreti,
            SonraOdeOran = res.SonraOdeOran,
            Aciklama = res.Aciklama,
            // Dönüşüm REZERVASYON FİYAT TAAHHÜDÜNÜ taşır — yeniden fiyatlama/kod doğrulaması YAPILMAZ
            // (müşteriye verilen fiyat dönüşümde değişmez). Kod + kaynak İZ olarak kopyalanır
            // (adversarial A5-B3: iz kopyalanmayınca indirimli tutarın gerekçesi denetimde kayboluyordu).
            Kaynak = res.Kaynak,
            KampanyaKodu = res.KampanyaKodu
        }, ct);
    }

    private async Task<bool> Transition(
        Guid id, ReservationStatus to, ReservationStatus[] allowedFrom, CancellationToken ct)
    {
        return await _repository.UpdateReservationAsync(id, r =>
        {
            BranchScope.RequireInScope(_currentUser, r.CikisOfisi); // adversarial M3 (Confirm/Cancel)
            if (Array.IndexOf(allowedFrom, r.Durum) < 0)
                throw new ValidationException($"Rezervasyon '{r.Durum}' durumundan '{to}' durumuna geçemez.");
            r.Durum = to;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }
}
