using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>
/// Kira sözleşmesi iş mantığı. Doğrudan kira oluşturma + iptal. Double-booking,
/// DB exclusion constraint ile garanti edilir (eşzamanlı istekte tek kazanan).
/// Liste rol bazlı şube kapsamıyla (çıkış ofisi).
/// </summary>
public sealed class RentalService(
    IBookingRepository repository,
    ICurrentUser currentUser,
    PricingService pricing,
    RentACar.Application.RentalAddOns.IRentalAddOnRepository addOnRepository)
{
    private readonly IBookingRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly PricingService _pricing = pricing;
    private readonly RentACar.Application.RentalAddOns.IRentalAddOnRepository _addOnRepository = addOnRepository;

    public Task<IReadOnlyList<RentalContract>> ListAsync(CancellationToken ct = default)
        => _repository.ListRentalsAsync(BranchScope.Effective(_currentUser), ct);

    /// <summary>Kira listesi: filtre + müşteri/araç/fatura-durumu. Rol bazlı şube kapsamı zorlanır.</summary>
    public Task<IReadOnlyList<RentalRow>> SearchAsync(RentalFilter filter, CancellationToken ct = default)
    {
        var scope = BranchScope.Effective(_currentUser);
        if (scope is not null) filter.Sube = scope; // operatör kendi şubesi dışına çıkamaz
        return _repository.SearchRentalRowsAsync(filter, ct);
    }

    public Task<RentalContract?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindRentalAsync(id, ct);

    public async Task<Guid> CreateDirectAsync(BookingInput input, CancellationToken ct = default)
    {
        BookingMath.Validate(input);
        var (gun, tutar) = await _pricing.PriceAsync(input, ct); // fiyat motoru: manuel >0 kazanır, yoksa tarife

        // Yumuşak ön-kontrol (kullanıcı dostu hata); kesin garanti exclusion constraint.
        if (await _repository.HasOverlappingActiveRentalAsync(input.VehicleId, input.BasTar, input.BitTar, null, ct))
            throw new AvailabilityConflictException();

        var contract = new RentalContract
        {
            Durum = RentalStatus.Kirada,
            MusteriId = input.MusteriId,
            VehicleId = input.VehicleId,
            BasTar = input.BasTar,
            BitTar = input.BitTar,
            CikisOfisi = input.CikisOfisi,
            DonusOfisi = input.DonusOfisi,
            Gun = gun,
            GunlukUcret = input.GunlukUcret,
            KmLimit = input.KmLimit,
            FazlaKmUcret = input.FazlaKmUcret,
            YakitBirimUcret = input.YakitBirimUcret,
            Tutar = tutar,
            GenelToplam = tutar,
            Tahsilat = 0m,
            Bakiye = tutar,
            Provizyon = input.Provizyon,
            Depozito = input.Depozito,
            KomisyonOran = input.KomisyonOran,
            KomisyonTutar = input.KomisyonTutar,
            DropUcreti = input.DropUcreti,
            SonraOdeOran = input.SonraOdeOran,
            Aciklama = input.Aciklama
        };
        await _repository.CreateRentalAsync(contract, ct);
        return contract.Id;
    }

    /// <summary>Teslim: araç çıkışında KM/yakıt girişi.</summary>
    public async Task<bool> DeliverAsync(Guid id, int cikisKm, int cikisYakit, CancellationToken ct = default)
    {
        return await _repository.UpdateRentalAsync(id, c =>
        {
            if (c.Durum != RentalStatus.Kirada)
                throw new ValidationException("Yalnız aktif (Kirada) sözleşmede teslim yapılır.");
            if (c.CikisKm is not null)
                throw new ValidationException("Araç zaten teslim edilmiş.");
            if (cikisKm < 0)
                throw new ValidationException("Çıkış KM negatif olamaz.");
            c.CikisKm = cikisKm;
            c.CikisYakit = cikisYakit;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>
    /// Dönüş: KM/yakıt/gerçek dönüş tarihi → fazla km, eksik yakıt, uzatma bedelleri;
    /// GenelToplam + Bakiye güncellenir; durum Tamamlandı (araç tekrar müsait olur).
    /// </summary>
    public async Task<bool> ReturnAsync(
        Guid id, int donusKm, int donusYakit, DateTimeOffset gercekDonus, CancellationToken ct = default)
    {
        // Ek hizmet brütü dönüşte GenelToplam'da KORUNMALI (yoksa düşer).
        var ekHizmetToplam = (await _addOnRepository.ListForRentalAsync(id, ct)).Sum(a => a.Toplam);
        return await _repository.UpdateRentalAsync(id, c =>
        {
            if (c.Durum != RentalStatus.Kirada)
                throw new ValidationException("Yalnız aktif (Kirada) sözleşmede dönüş yapılır.");
            if (c.CikisKm is null)
                throw new ValidationException("Önce teslim (çıkış KM) girilmelidir.");
            if (donusKm < c.CikisKm)
                throw new ValidationException("Dönüş KM, çıkış KM'den küçük olamaz.");
            if (gercekDonus < c.BasTar)
                throw new ValidationException("Dönüş tarihi başlangıçtan önce olamaz.");

            var r = ReturnMath.Compute(c, donusKm, donusYakit, gercekDonus);
            c.DonusKm = donusKm;
            c.DonusYakit = donusYakit;
            c.GercekDonusTar = gercekDonus;
            c.FazlaKm = r.FazlaKm;
            c.FazlaKmBedeli = r.FazlaKmBedeli;
            c.EksikYakit = r.EksikYakit;
            c.YakitBedeli = r.YakitBedeli;
            c.UzatmaGun = r.UzatmaGun;
            c.UzatmaBedeli = r.UzatmaBedeli;
            // r.GenelToplam = baz brüt (ek hizmet hariç); ek hizmet brütünü ekle (RentalTotals ile tutarlı).
            c.GenelToplam = r.GenelToplam + ekHizmetToplam;
            c.Bakiye = c.GenelToplam - c.Tahsilat;
            c.Durum = RentalStatus.Tamamlandi;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>
    /// Kira uzatma (roadmap I1): aktif (Kirada) sözleşmenin bitiş tarihini ileri iter; ek gün × günlük ücret
    /// kadar UzatmaBedeli + GenelToplam + Bakiye artar (ReturnAsync ile aynı operasyonel model — DEFTER POSTLAMAZ,
    /// kontrat bakiyesi güncellenir; tahsilat/fatura ayrı). Uzatılan aralıkta başka aktif kira çakışması red.
    /// </summary>
    public async Task<bool> ExtendAsync(Guid id, DateTimeOffset yeniBitTar, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var c = await _repository.FindRentalAsync(id, ct);
        if (c is null) return false;
        if (c.Durum != RentalStatus.Kirada)
            throw new ValidationException("Yalnız aktif (Kirada) sözleşme uzatılabilir.");
        if (yeniBitTar <= c.BitTar)
            throw new ValidationException("Yeni bitiş tarihi mevcut bitişten sonra olmalıdır.");

        // Uzatılan aralıkta (kendisi hariç) başka aktif kira çakışması olmamalı.
        if (await _repository.HasOverlappingActiveRentalAsync(c.VehicleId, c.BasTar, yeniBitTar, id, ct))
            throw new AvailabilityConflictException();

        return await _repository.UpdateRentalAsync(id, x =>
        {
            if (x.Durum != RentalStatus.Kirada)
                throw new ValidationException("Yalnız aktif (Kirada) sözleşme uzatılabilir.");
            if (yeniBitTar <= x.BitTar)
                throw new ValidationException("Yeni bitiş tarihi mevcut bitişten sonra olmalıdır.");

            var yeniGun = BookingMath.ComputeGun(x.BasTar, yeniBitTar);
            var ekGun = yeniGun - x.Gun;
            if (ekGun <= 0) throw new ValidationException("Uzatma en az 1 gün olmalıdır.");
            var ekBedel = ekGun * x.GunlukUcret;

            x.BitTar = yeniBitTar;
            x.Gun = yeniGun;
            x.UzatmaGun += ekGun;            // kümülatif (birden çok uzatma)
            x.UzatmaBedeli += ekBedel;
            x.Tutar += ekBedel;
            x.GenelToplam += ekBedel;
            x.Bakiye = x.GenelToplam - x.Tahsilat;
            x.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        return await _repository.UpdateRentalAsync(id, c =>
        {
            if (c.Durum != RentalStatus.Kirada)
                throw new ValidationException($"Kira '{c.Durum}' durumundayken iptal edilemez.");
            c.Durum = RentalStatus.Iptal;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }
}
