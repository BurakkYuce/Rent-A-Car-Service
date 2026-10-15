using RentACar.Application.Common;
using RentACar.Application.Fleet;

namespace RentACar.PublicSite.Components.Themes.Vitrin;

/// <summary>Ana sayfadaki segment kutusu: görünen ad, üye ilanlar (filodaki sırayla) ve kapak ilanı.</summary>
/// <param name="Key">Kalıcı anahtar: <c>luks</c>, <c>suv</c>, <c>elektrik</c> (css/test için).</param>
/// <param name="Ad">Görünen ad ("Lüks", "SUV", "Elektrikli" / "Hibrit" / "Elektrikli ve hibrit").</param>
/// <param name="Kartlar">Segmentteki ilanlar.</param>
/// <param name="Kapak">Kutunun fotoğrafı: segmentte fotoğraflı ve başka kutuda (ya da sahnede) kullanılmamış ilk
/// ilan; yoksa fotoğraflı ilk ilan; hiç yoksa null (kutu fotoğrafsız düşüşe geçer).</param>
public sealed record VitrinSegment(string Key, string Ad, IReadOnlyList<FleetShowcaseCard> Kartlar, FleetShowcaseCard? Kapak);

/// <summary>
/// Vitrin ana sayfasının segment vurgusu (lüks, SUV, elektrikli) — SAF fonksiyon; veri ilanın sitede görünür
/// özelliklerinden gelir (<see cref="FleetShowcaseCard.TumOzellikler"/>; yoksa kart çipleri). Ad ya da fiyat
/// tahmini YOK: marka listesine bakıp "lüks" demek firmanın söylemediği bir şeyi söylemek olurdu.
///
/// <para><b>Eşleşme:</b> değer kelime kelime (Türkçe harfler ASCII'ye çevrilmiş, küçük harf) karşılaştırılır,
/// alt dize değil. Lüks ve SUV yalnız sınıf/segment/kategori/kasa etiketlerinde aranır ("Lüks paket" gibi bir
/// donanım satırı aracı lüks yapmaz); elektrik/hibrit yalnız yakıt/enerji/motor etiketlerinde.</para>
///
/// <para><b>Basılmama kuralları (T2):</b> üyesi olmayan segment yok; filonun TAMAMINI kapsayan segment de yok
/// (her şey "lüks"se ayrı bir kutu hiçbir şeyi öne çıkarmaz). Hiç segment yoksa bölüm hiç basılmaz.</para>
/// </summary>
public static class VitrinSegments
{
    private static readonly string[] ClassLabels = ["segment", "sinif", "kategori", "kasa"];
    private static readonly string[] EnergyLabels = ["yakit", "enerji", "motor"];

    private static readonly string[] LuxuryWords = ["luks", "premium"];
    private static readonly string[] SuvWords = ["suv", "crossover", "arazi"];
    private static readonly string[] ElectricWords = ["elektrik", "elektrikli"];
    private static readonly string[] HybridWords = ["hibrit", "hybrid"];

    /// <param name="cards">Yayındaki ilanlar (vitrin sırası).</param>
    /// <param name="usedPhoto">Sahnede zaten büyük basılan fotoğraf; kutularda tekrar kullanılmamaya çalışılır.</param>
    public static IReadOnlyList<VitrinSegment> From(IReadOnlyList<FleetShowcaseCard> cards, Guid? usedPhoto = null)
    {
        var luxury = cards.Where(c => Has(c, ClassLabels, LuxuryWords)).ToList();
        var suv = cards.Where(c => Has(c, ClassLabels, SuvWords)).ToList();
        var electric = cards.Where(c => Has(c, EnergyLabels, ElectricWords)).ToList();
        var hybrid = cards.Where(c => Has(c, EnergyLabels, HybridWords)).ToList();
        var electrified = cards.Where(c => electric.Contains(c) || hybrid.Contains(c)).ToList();
        var electricName = (electric.Count > 0, hybrid.Count > 0) switch
        {
            (true, true) => "Elektrikli ve hibrit",
            (true, false) => "Elektrikli",
            _ => "Hibrit",
        };

        var used = new HashSet<Guid>();
        if (usedPhoto is { } u) used.Add(u);
        var result = new List<VitrinSegment>(3);
        foreach (var (key, name, members) in new[]
                 {
                     ("luks", "Lüks", luxury), ("suv", "SUV", suv), ("elektrik", electricName, electrified),
                 })
        {
            if (members.Count == 0 || members.Count == cards.Count) continue;
            var cover = members.FirstOrDefault(c => c.CoverPhotoId is { } p && !used.Contains(p))
                        ?? members.FirstOrDefault(c => c.CoverPhotoId is not null);
            if (cover?.CoverPhotoId is { } photo) used.Add(photo);
            result.Add(new VitrinSegment(key, name, members, cover));
        }
        return result;
    }

    private static bool Has(FleetShowcaseCard card, string[] labelRoots, string[] words)
    {
        foreach (var o in card.TumOzellikler ?? card.Ozellikler)
        {
            var label = TurkishText.Normalize(o.Etiket ?? "");
            if (!labelRoots.Any(label.Contains)) continue;
            var valueWords = Words(o.Deger);
            if (valueWords.Any(words.Contains)) return true;
        }
        return false;
    }

    private static IEnumerable<string> Words(string? value)
        => TurkishText.Normalize(value ?? "")
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static readonly char[] Separators = [' ', ',', ';', '/', '-', '(', ')', '.', '+', '&'];
}
