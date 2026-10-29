using System.Globalization;
using RentACar.Application.Fleet;

namespace RentACar.PublicSite.Components.Themes;

/// <summary>Süzgeç seçeneği: değer (ilandaki yazımıyla), o değerdeki ilan sayısı, seçili mi.</summary>
public sealed record FilterOption(string Value, int Count, bool Selected);

/// <summary>Bir süzgeç boyutu: sorgu adı (<c>vites</c>), görünen ad ("Vites"), seçenekler.</summary>
public sealed record FilterFacet(string Key, string Label, IReadOnlyList<FilterOption> Options);

/// <summary>
/// /musaitlik süzgecinin temaya verilen hâli: boyutlar, formun taşıması gereken arama alanları (tarih, saat, şube,
/// ilan — GET formu gönderilince arama kaybolmasın) ve etkin seçim sayısı.
/// </summary>
public sealed record FleetFilterModel(IReadOnlyList<FilterFacet> Facets, IReadOnlyList<KeyValuePair<string, string>> Keep,
    int Active)
{
    /// <summary>Süzgeci temizleyen adres: arama alanları korunur.</summary>
    public string ClearHref => "/musaitlik" + (Keep.Count == 0 ? ""
        : "?" + string.Join("&", Keep.Select(k => $"{Uri.EscapeDataString(k.Key)}={Uri.EscapeDataString(k.Value)}")));
}

/// <summary>
/// İlan süzgeci — vites, yakıt, koltuk, sınıf. Veri ilanın GÖRÜNÜR özellik satırlarından (etiket eşleşmesiyle;
/// ayrı bir sınıf alanı yok); bir boyut ancak en az iki farklı değer varsa sunulur (tek seçenekli süzgeç gürültü).
/// Boyut içinde VEYA, boyutlar arasında VE. Tanınmayan değer (elle yazılmış adres) yok sayılır: liste boşalmaz.
/// Saf: sayfa (Availability) çağırır, tema yalnız basar.
/// </summary>
public static class FleetFilter
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Boyutlar: sorgu adı, görünen ad, eşleşen özellik etiketleri (küçük harf).</summary>
    public static readonly IReadOnlyList<(string Key, string Label, string[] Labels)> Dimensions =
    [
        ("sinif", "Sınıf", ["sınıf", "segment", "kasa tipi", "kasa", "gövde tipi", "gövde", "araç sınıfı"]),
        ("vites", "Vites", ["vites", "vites tipi", "şanzıman"]),
        ("yakit", "Yakıt", ["yakıt", "yakıt tipi", "yakıt türü"]),
        ("koltuk", "Koltuk", ["koltuk", "koltuk sayısı", "kişi", "kişi sayısı", "yolcu", "kapasite"]),
    ];

    private static bool Same(string a, string b) => Tr.CompareInfo.Compare(a.Trim(), b.Trim(), CompareOptions.IgnoreCase) == 0;

    /// <summary>İlanın o boyuttaki değeri (ilk eşleşen etiket) ya da null.</summary>
    public static string? ValueOf(IEnumerable<FeatureItem> features, string key)
    {
        var labels = Dimensions.First(d => d.Key == key).Labels;
        return features.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Deger)
                && labels.Contains((f.Etiket ?? "").Trim().ToLower(Tr)))?.Deger!.Trim();
    }

    /// <summary>Sorgudaki seçimler (boyut başına birden çok değer; boşlar atılır).</summary>
    public static Dictionary<string, string[]> Parse(IReadOnlyDictionary<string, string[]?> query)
        => Dimensions.ToDictionary(d => d.Key,
            d => query.TryGetValue(d.Key, out var v) && v is not null
                ? v.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray()
                : []);

    /// <summary>Seçimin geçerli kısmı: yalnız ilanlarda gerçekten bulunan değerler.</summary>
    public static Dictionary<string, string[]> Effective(IReadOnlyList<ListingCard> all, IReadOnlyDictionary<string, string[]> selected)
        => Dimensions.ToDictionary(d => d.Key, d =>
        {
            var values = all.Select(c => ValueOf(c.AllFeatures, d.Key)).OfType<string>().ToList();
            return selected.TryGetValue(d.Key, out var s)
                ? s.Where(x => values.Any(v => Same(v, x))).ToArray()
                : [];
        });

    /// <summary>İlan seçime uyuyor mu (boyut içinde VEYA, boyutlar arasında VE).</summary>
    public static bool Matches(ListingCard card, IReadOnlyDictionary<string, string[]> effective)
        => effective.All(e => e.Value.Length == 0
            || (ValueOf(card.AllFeatures, e.Key) is { } v && e.Value.Any(x => Same(v, x))));

    /// <summary>Sunulacak boyutlar (en az iki değer); seçenek sayıları tüm ilanlar üzerinden.</summary>
    public static IReadOnlyList<FilterFacet> Facets(IReadOnlyList<ListingCard> all, IReadOnlyDictionary<string, string[]> effective)
    {
        var list = new List<FilterFacet>();
        foreach (var (key, label, _) in Dimensions)
        {
            var groups = new List<(string Value, int Count)>();
            foreach (var v in all.Select(c => ValueOf(c.AllFeatures, key)).OfType<string>())
            {
                var i = groups.FindIndex(g => Same(g.Value, v));
                if (i < 0) groups.Add((v, 1));
                else groups[i] = (groups[i].Value, groups[i].Count + 1);
            }
            if (groups.Count < 2) continue;
            var picked = effective.TryGetValue(key, out var s) ? s : [];
            list.Add(new FilterFacet(key, label, [.. groups
                .OrderBy(g => LeadingNumber(g.Value) ?? int.MaxValue)
                .ThenBy(g => g.Value, StringComparer.Create(Tr, ignoreCase: true))
                .Select(g => new FilterOption(g.Value, g.Count, picked.Any(p => Same(p, g.Value))))]));
        }
        return list;
    }

    /// <summary>Teknik satır: vites, yakıt, koltuk değerleri ("Manuel, Dizel, 5 kişilik"); hiçbiri yoksa null.</summary>
    public static string? SpecLine(ListingCard card)
    {
        var parts = new[] { "vites", "yakit", "koltuk" }.Select(k => ValueOf(card.AllFeatures, k)).OfType<string>().ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static int? LeadingNumber(string s)
    {
        var digits = new string(s.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
