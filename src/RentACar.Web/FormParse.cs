using System.Globalization;

namespace RentACar.Web;

/// <summary>
/// Form alanı ayrıştırma yardımcıları. Minimal-API'de nullable değer-tipi (<c>decimal?</c>,
/// <c>int?</c>, <c>DateTimeOffset?</c>, <c>Guid?</c>) parametreler BOŞ string ("") ile 400
/// verir. Bu yüzden opsiyonel sayısal/tarih alanları <c>string?</c> alınıp burada güvenle
/// ayrıştırılır: boş/whitespace → null.
/// </summary>
public static class FormParse
{
    public static decimal? Dec(string? s)
        => decimal.TryParse((s ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    public static int? Int(string? s)
        => int.TryParse((s ?? "").Trim(), out var i) ? i : null;

    // Form tarihi tz'siz gelir → TryParse yerel offset'li (+03:00) DateTimeOffset üretir; Npgsql
    // timestamptz YALNIZ UTC (offset 0) kabul eder → ToUniversalTime ile normalize (yoksa yazımda 500).
    public static DateTimeOffset? Date(string? s)
        => DateTimeOffset.TryParse((s ?? "").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToUniversalTime() : null;

    public static Guid? Id(string? s)
        => Guid.TryParse((s ?? "").Trim(), out var g) ? g : null;

    /// <summary>&lt;input type="date"&gt; → DateOnly (FAZ-45). Saat dilimi YOK: takvim günü olduğu gibi
    /// alınır; DateTimeOffset'e çevirip geri düşürmek gün kaymasına açık kapı bırakırdı.</summary>
    public static DateOnly? Day(string? s)
        => DateOnly.TryParse((s ?? "").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>&lt;input type="time"&gt; → TimeOnly ("HH:mm" ya da "HH:mm:ss").</summary>
    public static TimeOnly? Hour(string? s)
        => TimeOnly.TryParse((s ?? "").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>Opsiyonel form metni: boş/whitespace → null. Denetim DRY: 10 endpoint dosyasında birebir
    /// private kopyası vardı, buraya toplandı (11. kopya sapmayla doğmasın). Davranış AYNI — Trim YOK.</summary>
    public static string? Str(IFormCollection f, string key)
    {
        var v = f[key].ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
