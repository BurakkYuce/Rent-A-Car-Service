using RentACar.Application.Bookings;
using RentACar.Application.Vehicles;

namespace RentACar.Application.Pricing;

/// <summary>
/// Fiyat çözüm adaptörü (booking akışı için ince facade): rezervasyon/teklif/kira oluştururken EFEKTİF
/// günlük ücret + tutar çözer. TEK fiyat motoru = <see cref="RentalQuoteEngine"/> (tarife matrisi).
/// Kural: manuel ücret (>0) DAİMA kazanır (geriye-uyumlu); aksi halde aracın grubuna göre tarife
/// matrisinden (onaylı) günlük ücret çözülür. Eşleşme yoksa **geriye-uyum fallback**: eski
/// <see cref="RateCardService"/> (DEPRECATED — yeni tarifeler RateMatrix'e). Hiçbiri yoksa 0 (manuel girilir).
/// Defter/bakiye YAZMAZ — yalnız tutar hesaplar.
///
/// Yan etki: <see cref="PriceAsync"/> auto-fiyat bulduğunda input.GunlukUcret'i günceller (çağıran servis
/// efektif ücreti sözleşmeye yazsın diye). (roadmap A1: RentalQuoteEngine birincil; RateCard deprecate.)
///
/// KAPSAM (önemli): bu facade YALNIZ **günlük baz ücreti** çözer (tarife matrisi gün-kademesi). RentalRule
/// indirim/hediye-gün + sigorta/ek hizmet + KM aşım **uygulanmaz** — bunlar kira oluşturmada değil; tam
/// teklif `RentalQuoteEngine.QuoteAsync` / `/fiyat-hesapla` ekranında, KM aşım dönüşte (ReturnMath), ek
/// hizmet RentalAddOn'da hesaplanır. Sözleşme Tutar'ı = gün × baz günlük ücret (eskiden olduğu gibi).
/// Çok-döviz: matris TRY değilse auto-fiyat UYGULANMAZ (booking tek-döviz) → 0 (manuel girilir).
/// </summary>
public sealed class PricingService(
    IVehicleRepository vehicles, RentalQuoteEngine quoteEngine, RateCardService rateCards)
{
    private readonly IVehicleRepository _vehicles = vehicles;
    private readonly RentalQuoteEngine _quoteEngine = quoteEngine;
    private readonly RateCardService _rateCards = rateCards;

    /// <summary>
    /// Gün + tutar döner; gerekiyorsa input.GunlukUcret'i tarife matrisinden gelen efektif ücretle
    /// günceller. Manuel ücret verilmişse (&gt;0) motora/tarifeye bakılmaz.
    /// </summary>
    public async Task<(int Gun, decimal Tutar)> PriceAsync(BookingInput input, CancellationToken ct = default)
    {
        var gun = BookingMath.ComputeGun(input.BasTar, input.BitTar);

        if (input.GunlukUcret <= 0)
        {
            // CikisOfisi (şube) → şube-özel matris eşleşsin (HIGH-2).
            var rate = await ResolveDailyRateAsync(input.VehicleId, input.BasTar, input.BitTar, input.CikisOfisi, ct);
            if (rate > 0) input.GunlukUcret = rate;
        }

        return (gun, gun * input.GunlukUcret);
    }

    /// <summary>
    /// Aracın grubu + tarih aralığı için efektif günlük ücret. BİRİNCİL: <see cref="RentalQuoteEngine"/>
    /// (onaylı tarife matrisi gün-kademesi). Eşleşme yoksa geriye-uyum: eski RateCard (deprecated).
    /// Grup yoksa ya da hiçbir tarife yoksa 0.
    /// </summary>
    public async Task<decimal> ResolveDailyRateAsync(
        Guid vehicleId, DateTimeOffset basTar, DateTimeOffset bitTar, string? sube = null, CancellationToken ct = default)
    {
        var vehicle = await _vehicles.FindAsync(vehicleId, ct);
        if (vehicle?.Grup is not { } grup || string.IsNullOrWhiteSpace(grup)) return 0m;

        // Birincil: tarife matrisi motoru. Kanal=null (booking'de kanal yok) → engine "her kanalı eşle".
        if (bitTar > basTar)
        {
            var q = await _quoteEngine.QuoteAsync(
                new QuoteRequest { AracGrupKod = grup, Sube = sube, BasTar = basTar, BitTar = bitTar }, ct);
            // Matris EŞLEŞTİYSE (TarifeKodu dolu) onun sonucu kesin → RateCard fallback'e DÜŞME (MEDIUM-1).
            if (q.TarifeKodu is not null)
            {
                // Çok-döviz booking'de desteklenmiyor (RentalContract tek-döviz) → yalnız TRY auto-uygula (HIGH-1).
                return string.Equals(q.ParaBirimi, "TRY", StringComparison.OrdinalIgnoreCase) ? q.GunlukUcret : 0m;
            }
        }

        // Matris YOK → geriye-uyum fallback: eski RateCard (DEPRECATED).
        var gun = BookingMath.ComputeGun(basTar, bitTar);
#pragma warning disable CS0618 // RateCard fiyat çözümü deprecated; bilinçli geriye-uyum fallback'i.
        var card = await _rateCards.GetRateAsync(grup, gun, basTar, ct);
#pragma warning restore CS0618
        return card?.GunlukUcret ?? 0m;
    }
}
