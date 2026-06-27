using RentACar.Application.Bookings;
using RentACar.Application.Vehicles;

namespace RentACar.Application.Pricing;

/// <summary>
/// Fiyat motoru v1: rezervasyon/teklif/kira için EFEKTİF günlük ücret + tutar çözer.
/// Kural: manuel ücret (>0) DAİMA kazanır (geriye-uyumlu); aksi halde aracın grubuna +
/// gün kademesine + başlangıç tarihine göre Tarife (RateCard) lookup. Eşleşme yoksa 0
/// (kullanıcı manuel girebilir). Defter/bakiye YAZMAZ — yalnız tutar hesaplar.
///
/// Yan etki: <see cref="PriceAsync"/> auto-fiyat bulduğunda input.GunlukUcret'i günceller
/// (çağıran servis efektif ücreti sözleşmeye yazsın diye).
/// </summary>
public sealed class PricingService(IVehicleRepository vehicles, RateCardService rateCards)
{
    private readonly IVehicleRepository _vehicles = vehicles;
    private readonly RateCardService _rateCards = rateCards;

    /// <summary>
    /// Gün + tutar döner; gerekiyorsa input.GunlukUcret'i tarifeden gelen efektif ücretle
    /// günceller. Manuel ücret verilmişse (&gt;0) tarifeye bakılmaz.
    /// </summary>
    public async Task<(int Gun, decimal Tutar)> PriceAsync(BookingInput input, CancellationToken ct = default)
    {
        var gun = BookingMath.ComputeGun(input.BasTar, input.BitTar);

        if (input.GunlukUcret <= 0)
        {
            var rate = await ResolveDailyRateAsync(input.VehicleId, gun, input.BasTar, ct);
            if (rate > 0) input.GunlukUcret = rate;
        }

        return (gun, gun * input.GunlukUcret);
    }

    /// <summary>
    /// Aracın grubu + gün kademesi + tarih için tarifeden günlük ücret. Grup yoksa ya da
    /// eşleşen tarife yoksa 0. (Tarife seçim kuralı RateCardService.GetRateAsync'te.)
    /// </summary>
    public async Task<decimal> ResolveDailyRateAsync(
        Guid vehicleId, int gun, DateTimeOffset tarih, CancellationToken ct = default)
    {
        var vehicle = await _vehicles.FindAsync(vehicleId, ct);
        if (vehicle?.Grup is not { } grup || string.IsNullOrWhiteSpace(grup)) return 0m;

        var card = await _rateCards.GetRateAsync(grup, gun, tarih, ct);
        return card?.GunlukUcret ?? 0m;
    }
}
