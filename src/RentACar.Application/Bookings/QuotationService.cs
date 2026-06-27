using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>
/// Teklif (quotation) iş mantığı + durum makinesi (Taslak→Gonderildi→Kabul/Red).
/// Gün/tutar BookingMath ile (rezervasyonla aynı hesap). Kabul = rezervasyona dönüştür.
/// Yazma OperationsWrite gerektirir; liste rol bazlı şube kapsamıyla (çıkış ofisi).
/// </summary>
public sealed class QuotationService(IQuotationRepository repository, ICurrentUser currentUser, PricingService pricing)
{
    private readonly IQuotationRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly PricingService _pricing = pricing;

    public Task<IReadOnlyList<Quotation>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(BranchScope.Effective(_currentUser), ct);

    public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(QuotationInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var booking = input.ToBooking();
        BookingMath.Validate(booking);
        // Fiyat motoru: manuel >0 kazanır, yoksa tarife → booking.GunlukUcret efektif ücretle güncellenir.
        var (gun, tutar) = await _pricing.PriceAsync(booking, ct);
        if (input.GecerlilikTarihi is { } g && g < input.BasTar)
            throw new ValidationException("Geçerlilik tarihi başlangıç tarihinden önce olamaz.");

        var quotation = new Quotation
        {
            Durum = QuotationStatus.Taslak,
            MusteriId = input.MusteriId,
            VehicleId = input.VehicleId,
            BasTar = input.BasTar,
            BitTar = input.BitTar,
            CikisOfisi = input.CikisOfisi,
            DonusOfisi = input.DonusOfisi,
            Gun = gun,
            GunlukUcret = booking.GunlukUcret, // efektif ücret (fiyat motoru sonrası)
            Tutar = tutar,
            KmLimit = input.KmLimit,
            FazlaKmUcret = input.FazlaKmUcret,
            YakitBirimUcret = input.YakitBirimUcret,
            GecerlilikTarihi = input.GecerlilikTarihi,
            Aciklama = input.Aciklama
        };
        await _repository.CreateAsync(quotation, ct);
        return quotation.Id;
    }

    public Task<bool> SendAsync(Guid id, CancellationToken ct = default)
        => Transition(id, QuotationStatus.Gonderildi, [QuotationStatus.Taslak], ct);

    public Task<bool> RejectAsync(Guid id, CancellationToken ct = default)
        => Transition(id, QuotationStatus.Red, [QuotationStatus.Taslak, QuotationStatus.Gonderildi], ct);

    /// <summary>Kabul: teklifi rezervasyona çevirir (Reservation Id döner). Taslak/Gönderildi'den.</summary>
    public async Task<Guid> AcceptAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var quotation = await _repository.FindAsync(id, ct)
            ?? throw new ValidationException("Teklif bulunamadı.");
        if (quotation.Durum is not (QuotationStatus.Taslak or QuotationStatus.Gonderildi))
            throw new ValidationException("Yalnız Taslak/Gönderildi teklif kabul edilebilir.");

        return await _repository.ConvertToReservationAsync(id, q => new Reservation
        {
            Durum = ReservationStatus.Rezerv,
            MusteriId = q.MusteriId,
            VehicleId = q.VehicleId,
            BasTar = q.BasTar,
            BitTar = q.BitTar,
            CikisOfisi = q.CikisOfisi,
            DonusOfisi = q.DonusOfisi,
            Gun = q.Gun,
            GunlukUcret = q.GunlukUcret,
            Tutar = q.Tutar,
            KmLimit = q.KmLimit,
            FazlaKmUcret = q.FazlaKmUcret,
            YakitBirimUcret = q.YakitBirimUcret,
            Aciklama = q.Aciklama
        }, ct);
    }

    private async Task<bool> Transition(
        Guid id, QuotationStatus to, QuotationStatus[] allowedFrom, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return await _repository.UpdateAsync(id, q =>
        {
            if (Array.IndexOf(allowedFrom, q.Durum) < 0)
                throw new ValidationException($"Teklif '{q.Durum}' durumundan '{to}' durumuna geçemez.");
            q.Durum = to;
            q.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }
}
