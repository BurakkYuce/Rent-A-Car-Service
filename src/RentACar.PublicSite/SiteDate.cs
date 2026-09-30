using System.Globalization;

namespace RentACar.PublicSite;

/// <summary>Sitenin "bugün"ü — İSTANBUL günü (UTC değil: gece yarısından sonraki üç saatte UTC hâlâ dünü
/// gösterir). Tarih alanlarının <c>min</c> değeri buradan: geçmiş bir tarihe rezervasyon alınmıyor.</summary>
public static class SiteDate
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    /// <summary>"yyyy-MM-dd" (HTML date alanı biçimi).</summary>
    public static string TodayIso => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Istanbul)
        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
