using System.Globalization;

namespace RentACar.PublicSite;

/// <summary>Sitenin "bugün"ü — İSTANBUL günü (UTC değil: gece yarısından sonraki üç saatte UTC hâlâ dünü
/// gösterir). Tarih alanlarının <c>min</c> değeri buradan: geçmiş bir tarihe rezervasyon alınmıyor.</summary>
public static class SiteDate
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    /// <summary>Arama formunun varsayılan alış/iade saati.</summary>
    public const string DefaultTime = "10:00";

    /// <summary>Varsayılan kiralama süresi (gün): alış yarın, iade üç gün sonra.</summary>
    public const int DefaultDays = 3;

    /// <summary>"yyyy-MM-dd" (HTML date alanı biçimi).</summary>
    public static string TodayIso => Today(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>İstanbul'da bugünün haftanın günü ("bugün açık" satırı için).</summary>
    public static DayOfWeek TodayDayOfWeek => Today(DateTimeOffset.UtcNow).DayOfWeek;

    /// <summary>Verilen andaki İstanbul günü.</summary>
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Istanbul).DateTime);

    /// <summary>"yyyy-MM-dd" → gün; biçimsizse null.</summary>
    public static DateOnly? ParseDay(string? s)
        => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>"HH:mm" → saat; biçimsizse null (HTML time alanı bu biçimi gönderir).</summary>
    public static TimeOnly? ParseTime(string? s)
        => TimeOnly.TryParseExact(s, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>İstanbul'daki gün + saat → mutlak an (ofset İstanbul'un o andaki ofseti).</summary>
    public static DateTimeOffset At(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time);
        return new DateTimeOffset(local, Istanbul.GetUtcOffset(local));
    }

    /// <summary>
    /// Arama formunun dolu gelen tarihleri: girilen değer korunur; boşsa alış = yarın (İstanbul), iade = alış +
    /// <see cref="DefaultDays"/>. Biçimsiz alış tarihi de yarına düşer (form zaten geçersiz değeri basamaz).
    /// </summary>
    public static (string Bas, string Bit) FormDates(string? bas, string? bit, DateTimeOffset now)
    {
        var start = ParseDay(bas) ?? Today(now).AddDays(1);
        var end = ParseDay(bit) ?? start.AddDays(DefaultDays);
        return (Iso(start), Iso(end));
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
