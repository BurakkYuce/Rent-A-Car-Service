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

    public static DateTimeOffset? Date(string? s)
        => DateTimeOffset.TryParse((s ?? "").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static Guid? Id(string? s)
        => Guid.TryParse((s ?? "").Trim(), out var g) ? g : null;
}
