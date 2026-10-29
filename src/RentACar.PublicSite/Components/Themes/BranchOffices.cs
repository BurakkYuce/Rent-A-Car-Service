using System.Globalization;
using System.Text.RegularExpressions;
using RentACar.Application.Common;
using RentACar.Application.Fleet;
using RentACar.Domain.Entities;

namespace RentACar.PublicSite.Components.Themes;

/// <summary>Ofis/şube bloğunun bir satırı (temalar aynı kuraldan kurar).</summary>
/// <param name="Source">Satırın şubesi; firma adresinden kurulan satırda null.</param>
public sealed record BranchOffice(string Name, string? Address, IReadOnlyList<string> Hours, string? Phone,
    string? TelLink, string? MapLink, Branch? Source = null);

/// <summary>
/// Aktif şubelerden ofis bloğu ve "bugün açık" satırı. Veri yoksa boş/null döner — tema bölümü basmaz (T2).
/// Çalışma saati şubede serbest metin (<see cref="Branch.CalismaSaatleri"/>, <see cref="Branch.HaftalikCalismaSaatleri"/>);
/// bugünkü saat yalnız metinden KESİN okunabiliyorsa verilir, tahmin edilmez.
/// </summary>
public static partial class BranchOffices
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Site sırası (WebSira), sonra ad.</summary>
    public static IEnumerable<Branch> Ordered(IEnumerable<Branch> branches)
        => branches.OrderBy(b => b.WebSira ?? int.MaxValue).ThenBy(b => b.Ad, StringComparer.Create(Tr, ignoreCase: true));

    /// <summary>Sitede görünen şube adı (web adı varsa o).</summary>
    public static string NameOf(Branch b) => string.IsNullOrWhiteSpace(b.WebIsim) ? b.Ad : b.WebIsim!.Trim();

    /// <summary>Aktif şubeler (site sırası); adres, saat ya da telefonu olmayan şube basılmaz. Şube verisi yoksa
    /// firmanın genel adresi tek satır olarak; o da yoksa liste boş.</summary>
    public static List<BranchOffice> Build(IEnumerable<Branch> branches, FleetBranding? branding, int max)
    {
        var list = Ordered(branches)
            .Select(b => new BranchOffice(NameOf(b), AddressOf(b), HoursOf(b), Empty(b.Telefon), PhoneLink.Tel(b.Telefon), MapOf(b), b))
            .Where(o => o.Address is not null || o.Hours.Count > 0 || o.TelLink is not null)
            .Take(max)
            .ToList();
        if (list.Count == 0 && Empty(branding?.Address) is { } address)
            list.Add(new BranchOffice("Adresimiz", address, [], null, null, MapQuery(address)));
        return list;
    }

    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>"Atatürk Cd. No 5, Esenyurt/İstanbul" — il/ilçe adreste zaten geçiyorsa tekrar eklenmez.</summary>
    public static string? AddressOf(Branch b)
    {
        var street = Empty(b.Adres);
        var area = (Empty(b.Ilce), Empty(b.Il)) switch
        {
            ({ } d, { } c) => $"{d}/{c}",
            ({ } d, null) => d,
            (null, { } c) => c,
            _ => null,
        };
        if (street is null) return area;
        if (area is null || Tr.CompareInfo.IndexOf(street, Empty(b.Il) ?? area, CompareOptions.IgnoreCase) >= 0) return street;
        return $"{street}, {area}";
    }

    /// <summary>Çalışma saati: kısa alan ("09:00-18:00"), yoksa haftalık metin (gün başına satır).</summary>
    private static IReadOnlyList<string> HoursOf(Branch b)
        => (Empty(b.CalismaSaatleri) ?? Empty(b.HaftalikCalismaSaatleri)) is { } text
            ? [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];

    /// <summary>Konum varsa koordinatla (kesin nokta), yoksa adres metniyle harita araması.</summary>
    public static string? MapOf(Branch b)
    {
        if (b.Enlem is { } lat && b.Boylam is { } lng)
            return "https://www.google.com/maps/search/?api=1&query="
                   + Uri.EscapeDataString(string.Create(CultureInfo.InvariantCulture, $"{lat},{lng}"));
        return AddressOf(b) is { } address && Empty(b.Adres) is not null ? MapQuery(address) : null;
    }

    public static string MapQuery(string address)
        => "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(address);

    // ---- Bugünkü çalışma saati ----

    /// <summary>Gün adları, ISO sırasıyla (Pazartesi = 0): schema.org kısaltması, Türkçe ad, Türkçe kısaltmalar.</summary>
    private static readonly string[][] DayTokens =
    [
        ["mo", "pazartesi", "pzt", "pts"],
        ["tu", "salı", "sal", "sali"],
        ["we", "çarşamba", "çar", "carsamba", "car", "çrş"],
        ["th", "perşembe", "per", "persembe", "prş"],
        ["fr", "cuma", "cum"],
        ["sa", "cumartesi", "cmt", "cts"],
        ["su", "pazar", "paz", "pzr"],
    ];

    [GeneratedRegex(@"^(?<days>[^\d]*?)\s*:?\s*(?<h1>\d{1,2})[:.](?<m1>\d{2})\s*[-–—]\s*(?<h2>\d{1,2})[:.](?<m2>\d{2})\s*$")]
    private static partial Regex RangePart();

    [GeneratedRegex(@"^(?<days>.*?)\s*:?\s*kapal[ıi]\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex ClosedPart();

    /// <summary>"7/24 açık", "7 / 24", "24 saat açık" — her gün tüm gün.</summary>
    [GeneratedRegex(@"^(7\s*/\s*24|24\s*saat)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AllDay();

    /// <summary>Gün grubu sözcükleri (ISO günleri).</summary>
    private static readonly Dictionary<string, int[]> DayPhrases = new(StringComparer.Ordinal)
    {
        ["her gün"] = [0, 1, 2, 3, 4, 5, 6], ["hergün"] = [0, 1, 2, 3, 4, 5, 6], ["her gun"] = [0, 1, 2, 3, 4, 5, 6],
        ["hafta içi"] = [0, 1, 2, 3, 4], ["haftaiçi"] = [0, 1, 2, 3, 4], ["hafta ici"] = [0, 1, 2, 3, 4],
        ["hafta sonu"] = [5, 6], ["haftasonu"] = [5, 6],
    };

    /// <summary>
    /// Şubenin <paramref name="day"/> günündeki çalışma saati ("08:00–21:00") ya da null (bilinmiyor / kapalı).
    /// Okunan biçimler (satır satır; satır içinde virgülle ayrılmış birden çok kural): "09:00-18:00" (her gün),
    /// "Her gün 08:00-21:00", "Hafta içi 08:30-19:00, Cumartesi 09:00-17:00", "Mo-Sa 09:00-19:00" (schema.org),
    /// "Pazartesi: 09:00-18:00", "Pzt-Cuma 09:00-18:00", "Pazar kapalı". Güne özel kural günsüz kuraldan önce gelir;
    /// tanınmayan kural yok sayılır (saat uydurulmaz). Haftalık metin kısa alandan önce okunur (daha ayrıntılı).
    /// </summary>
    public static string? TodayHours(Branch b, DayOfWeek day)
    {
        var iso = ((int)day + 6) % 7;
        string? everyDay = null;
        foreach (var text in new[] { b.HaftalikCalismaSaatleri, b.CalismaSaatleri })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // "Mo,We 09:00-18:00": saatsiz parça sonraki parçanın günlerine eklenir.
                var pending = "";
                foreach (var part in line.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (ClosedPart().Match(part) is { Success: true } closed)
                    {
                        if (Days(pending + closed.Groups["days"].Value) is { Count: > 0 } off && off.Contains(iso)) return null;
                        pending = "";
                        continue;
                    }
                    if (AllDay().IsMatch(part))
                    {
                        everyDay ??= "24 saat";
                        pending = "";
                        continue;
                    }
                    var m = RangePart().Match(part);
                    if (!m.Success)
                    {
                        pending += part + ",";
                        continue;
                    }
                    var days = Days(pending + m.Groups["days"].Value);
                    pending = "";
                    if (Range(m) is not { } range || days is null) continue; // tanınmayan gün yazımı / saat
                    if (days.Count == 0) everyDay ??= range;                 // günsüz kural: her gün
                    else if (days.Contains(iso)) return range;
                }
            }
        }
        return everyDay;
    }

    private static string? Range(Match m)
    {
        static int N(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);
        var (h1, m1, h2, m2) = (N(m.Groups["h1"]), N(m.Groups["m1"]), N(m.Groups["h2"]), N(m.Groups["m2"]));
        if (h1 > 23 || h2 > 24 || m1 > 59 || m2 > 59) return null;
        return $"{h1:00}:{m1:00}–{h2:00}:{m2:00}";
    }

    /// <summary>Gün ifadesi → ISO gün kümesi; boş ifade → boş küme (her gün); tanınmayan sözcük → null.</summary>
    private static HashSet<int>? Days(string spec)
    {
        var set = new HashSet<int>();
        var text = spec.Trim().TrimEnd(':').Trim().ToLower(Tr);
        if (text.Length == 0) return set;
        foreach (var part in text.Split([',', ';', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (DayPhrases.TryGetValue(part, out var group))
            {
                set.UnionWith(group);
                continue;
            }
            var ends = part.Split(['-', '–', '—'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ends.Length is 0 or > 2) return null;
            var from = DayIndex(ends[0]);
            var to = ends.Length == 2 ? DayIndex(ends[1]) : from;
            if (from is null || to is null) return null;
            for (var d = from.Value; ; d = (d + 1) % 7)
            {
                set.Add(d);
                if (d == to) break;
            }
        }
        return set;
    }

    private static int? DayIndex(string token)
    {
        var t = token.Trim().TrimEnd('.');
        for (var i = 0; i < DayTokens.Length; i++)
            if (DayTokens[i].Contains(t, StringComparer.Ordinal)) return i;
        return null;
    }
}
