namespace RentACar.Application.Reporting;

/// <summary>
/// Sınıf (Grup) ortalaması — ORTAK hesap (iki kopya yasak): 2.2 tut/sat kural-b'nin gider/değer-oranı
/// ortalaması ve 2.3 SinifEndeks'in km-maliyet ortalaması BU helper'dan geçer. Yalnız değeri olan
/// üyeler ortalamaya girer (null → üye sayılmaz); grup adı Trim + OrdinalIgnoreCase; boşsa dışarıda.
/// </summary>
public static class GroupAverage
{
    public static Dictionary<string, decimal> Calculate<T>(
        IEnumerable<T> members, Func<T, string?> group, Func<T, decimal?> value)
        => members.Select(u => (Grup: group(u), Deger: value(u)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Grup) && x.Deger is not null)
            .GroupBy(x => x.Grup!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Average(x => x.Deger!.Value), StringComparer.OrdinalIgnoreCase);

    /// <summary>Sözlükten grup değeri (Trim anahtarıyla); grup boş/karşılıksız → null.</summary>
    public static decimal? Value(Dictionary<string, decimal> averages, string? group)
        => !string.IsNullOrWhiteSpace(group) && averages.TryGetValue(group.Trim(), out var v) ? v : null;
}
