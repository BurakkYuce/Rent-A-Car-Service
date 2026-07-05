using System.Globalization;
using System.Xml.Linq;
using RentACar.Domain.Entities;

namespace RentACar.Web.Kur;

/// <summary>
/// TCMB today.xml → KurKaydi listesi. SAF (networksüz → testlenebilir).
/// ⚠️ Ondalık ayıraç TCMB'de `.` → INVARIANT culture ZORUNLU (tr-TR ile 34.12 → 3412, sessiz 100× hata).
/// </summary>
public static class TcmbKurParser
{
    public static IReadOnlyList<KurKaydi> Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root; // <Tarih_Date Tarih="dd.mm.yyyy">
        var tarih = ParseTarih(root?.Attribute("Tarih")?.Value);
        var list = new List<KurKaydi>();
        foreach (var c in root?.Elements("Currency") ?? [])
        {
            var kod = (c.Attribute("Kod")?.Value ?? c.Attribute("CurrencyCode")?.Value ?? "").Trim().ToUpperInvariant();
            if (kod.Length is < 2 or > 3) continue;
            list.Add(new KurKaydi
            {
                Tarih = tarih,
                Kod = kod,
                Ad = (c.Element("Isim")?.Value ?? "").Trim(),
                Birim = ParseInt(c.Element("Unit")?.Value),
                ForexAlis = ParseDec(c.Element("ForexBuying")?.Value),
                ForexSatis = ParseDec(c.Element("ForexSelling")?.Value),
                EfektifAlis = ParseDec(c.Element("BanknoteBuying")?.Value),
                EfektifSatis = ParseDec(c.Element("BanknoteSelling")?.Value),
            });
        }
        return list;
    }

    /// <summary>TCMB "dd.mm.yyyy" → UTC gün başı. Parse edilemezse bugünün UTC günü.</summary>
    public static DateTimeOffset ParseTarih(string? s)
    {
        if (DateTime.TryParseExact((s ?? "").Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero);
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private static decimal? ParseDec(string? s)
        => string.IsNullOrWhiteSpace(s) ? null
           : decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int ParseInt(string? s)
        => int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i > 0 ? i : 1;
}
