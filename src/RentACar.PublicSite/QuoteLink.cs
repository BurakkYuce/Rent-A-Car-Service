using System.Globalization;

namespace RentACar.PublicSite;

/// <summary>
/// "Teklif iste" adresi — kart ve detay AYNI kuraldan kurar (eskiden yalnız /musaitlik'te vardı).
/// PR-8/14: talep formuna bağlam taşır: İLAN + tarihler + ziyaretçinin GÖRDÜĞÜ fiyat (snapshot).
/// Fiyat yalnız bilgi amaçlı taşınır; sunucu fiyatı ilandan yeniden okur (sorgudaki değer süstür).
/// Fiyat InvariantCulture ile yazılır — tr-TR virgülü sorguda yanlış ayrıştırılırdı.
/// </summary>
public static class QuoteLink
{
    public static string Build(string slug, string? start = null, string? end = null, string? branch = null,
        decimal? dailyPrice = null)
    {
        var q = System.Web.HttpUtility.ParseQueryString(string.Empty);
        q["ilan"] = slug; // slug: form ilanı yeniden çözer (yayından kalkmışsa fiyat/başlık taşınmaz)
        if (!string.IsNullOrWhiteSpace(start)) q["bas"] = start;
        if (!string.IsNullOrWhiteSpace(end)) q["bit"] = end;
        if (!string.IsNullOrWhiteSpace(branch)) q["sube"] = branch;
        if (dailyPrice is { } p) q["fiyat"] = p.ToString(CultureInfo.InvariantCulture);
        return "/rezervasyon-talebi?" + q;
    }
}
