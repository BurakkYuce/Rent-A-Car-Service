using RentACar.Domain.Entities;

namespace RentACar.Application.Bookings;

/// <summary>
/// Kiraya yazılan km hakkı + aşım ücreti — TEK kural (kabul bulgusu d-rapor-km-detay-03 + #366 adversarial).
/// Doğrudan kira, rezervasyondan dönüşüm, açık kira düzenlemesi ve canlı önizleme bu fonksiyonu çağırır.
///
/// <para><b>Üç ayrı girdi anlamı (M2 — "0" artık iki anlam taşımaz):</b>
/// <list type="bullet">
/// <item><b>Km sınırsız</b> (açık bayrak ya da KmSinirsiz rezervasyon kaynağı) → limit 0, hak uygulanmaz,
/// grup/tarife kopyalanmaz.</item>
/// <item><b>Elle girilen limit</b> (&gt; 0) → TOPLAM limit olarak o değer; günlük snapshot yok (uzatmada büyümez).</item>
/// <item><b>Boş limit</b> (0) → kuralın GÜNLÜK limiti (tarife kademesi önce, araç grubu sonra — motorla aynı
/// öncelik, M1) × gün; günlük değer <see cref="Result.KmLimitGunluk"/> olarak saklanır ve hak
/// günlük × (Gun + UzatmaGun) olarak büyür (H1).</item>
/// </list></para>
///
/// <para><b>Ücret:</b> elle girilen (&gt; 0) kazanır; boşsa kuralın ücreti. Dövizli kirada TL kural ücreti
/// KOPYALANMAZ (birim karışması — FeeLineService ile aynı) ve <see cref="Result.FxFeeSkipped"/> işaretlenir.</para>
/// </summary>
public static class GroupKmPolicy
{
    public readonly record struct Result(
        int KmLimit, decimal FazlaKmUcret, int? KmLimitGunluk, bool KmSinirsiz, bool FxFeeSkipped = false);

    public static Result Resolve(
        (int? DailyLimit, decimal? Fee) rule, int day, int enteredKmLimit, decimal enteredOverageFee,
        string? currency, ReservationSource? source, bool kmUnlimited)
    {
        if (kmUnlimited || source is { KmSinirsiz: true })
            return new Result(0, enteredOverageFee, null, true);

        if (enteredKmLimit > 0)
            return new Result(enteredKmLimit, Fee(rule, enteredOverageFee, currency, out var s1), null, false, s1);

        if (rule.DailyLimit is > 0 and var daily && day > 0)
        {
            var total = (int)Math.Min(int.MaxValue, (long)daily * day);
            return new Result(total, Fee(rule, enteredOverageFee, currency, out var s2), daily, false, s2);
        }

        // Kural yok → limit uygulanmaz (eski davranış); bayrak AÇIK değil — kural sonradan tanımlanırsa
        // düzenlemede uygulanabilsin.
        return new Result(0, enteredOverageFee, null, false);
    }

    /// <summary>Hak (toplam serbest km): günlük snapshot varsa günlük × (Gun + ek gün); yoksa KmLimit.</summary>
    public static int Entitlement(RentalContract c, int extraDays = 0)
        => c.KmLimitGunluk is > 0 and var daily
            ? (int)Math.Min(int.MaxValue, (long)daily * Math.Max(0, c.Gun + extraDays))
            : c.KmLimit;

    /// <summary>Dövizli kirada kural ücreti kopyalanmadığında forma düşen kısa not (L2).</summary>
    public const string FxFeeNote =
        "Dövizli kirada araç grubu/tarife km aşım ücreti (TL) kopyalanmaz — gerekiyorsa fazla km ücretini kirada elle girin.";

    private static decimal Fee((int? DailyLimit, decimal? Fee) rule, decimal entered, string? currency, out bool fxSkipped)
    {
        fxSkipped = false;
        if (entered > 0m) return entered;
        if (rule.Fee is not > 0m) return entered;
        if (FeeLineService.IsFx(currency)) { fxSkipped = true; return entered; }
        return rule.Fee.Value;
    }
}
