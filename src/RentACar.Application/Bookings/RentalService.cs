using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>
/// Kira sözleşmesi iş mantığı. Doğrudan kira oluşturma + iptal. Double-booking,
/// DB exclusion constraint ile garanti edilir (eşzamanlı istekte tek kazanan).
/// Teslim/dönüş PR #4'te.
/// </summary>
public sealed class RentalService(IBookingRepository repository)
{
    private readonly IBookingRepository _repository = repository;

    public Task<IReadOnlyList<RentalContract>> ListAsync(CancellationToken ct = default)
        => _repository.ListRentalsAsync(ct);

    public Task<RentalContract?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindRentalAsync(id, ct);

    public async Task<Guid> CreateDirectAsync(BookingInput input, CancellationToken ct = default)
    {
        BookingMath.Validate(input);
        var (gun, tutar) = BookingMath.Compute(input);

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
            c.GenelToplam = r.GenelToplam;
            c.Bakiye = r.GenelToplam - c.Tahsilat;
            c.Durum = RentalStatus.Tamamlandi;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
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
