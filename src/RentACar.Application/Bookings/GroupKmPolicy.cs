using RentACar.Application.ReservationSources;
using RentACar.Domain.Entities;

namespace RentACar.Application.Bookings;

/// <summary>
/// Kira açılırken km limiti + aşım ücreti çözümü — TEK kural (kabul bulgusu d-rapor-km-detay-03).
/// Doğrudan kira (<see cref="RentalService.CreateDirectAsync"/>), rezervasyondan dönüşüm
/// (<see cref="ReservationService.ConvertToRentalAsync"/>) ve canlı önizleme
/// (<see cref="RentalCalculationService"/>) bu fonksiyonu çağırır; kopyası yoktur.
///
/// <para><b>Öncelik (alan bazında):</b> elle girilen değer (&gt; 0) kazanır; boşsa (0) grubun
/// <c>GunlukKmLimiti × gün</c> ve <c>AsimKmUcreti</c> kiraya SNAPSHOT olarak yazılır (grup sonradan
/// değişirse açık kira etkilenmez). KM sınırsız kaynak kuralı (FAZ-49) en son uygulanır ve limiti 0'a çeker.</para>
///
/// <para><b>Döviz:</b> grup aşım ücreti TL'dir; dövizli kirada ücret KOPYALANMAZ (120 TL'yi 120 EUR saymak
/// FeeLineService'in reddettiği birim karışması). Limit yine kopyalanır.</para>
/// </summary>
public static class GroupKmPolicy
{
    public readonly record struct Result(int KmLimit, decimal FazlaKmUcret);

    public static Result Resolve(
        VehicleGroup? group, int day, int enteredKmLimit, decimal enteredOverageFee, string? currency,
        ReservationSource? source)
    {
        var kmLimit = enteredKmLimit;
        if (kmLimit <= 0 && group?.GunlukKmLimiti is > 0 and var daily && day > 0)
            kmLimit = (int)Math.Min(int.MaxValue, (long)daily * day);

        var fee = enteredOverageFee;
        if (fee <= 0m && kmLimit > 0 && group?.AsimKmUcreti is > 0m and var groupFee && !FeeLineService.IsFx(currency))
            fee = groupFee;

        return new Result(ReservationSourceRule.ApplyKmLimit(source, kmLimit), fee);
    }
}
