namespace RentACar.Application.Reporting;

/// <summary>
/// Sınıf (Grup) ortalaması — ORTAK hesap (iki kopya yasak): 2.2 tut/sat kural-b'nin gider/değer-oranı
/// ortalaması ve 2.3 SinifEndeks'in km-maliyet ortalaması BU helper'dan geçer. Yalnız değeri olan
/// üyeler ortalamaya girer (null → üye sayılmaz); grup adı Trim + OrdinalIgnoreCase; boşsa dışarıda.
/// </summary>
public static class GrupOrtalama
{
    public static Dictionary<string, decimal> Hesapla<T>(
        IEnumerable<T> uyeler, Func<T, string?> grup, Func<T, decimal?> deger)
        => uyeler.Select(u => (Grup: grup(u), Deger: deger(u)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Grup) && x.Deger is not null)
            .GroupBy(x => x.Grup!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Average(x => x.Deger!.Value), StringComparer.OrdinalIgnoreCase);

    /// <summary>Sözlükten grup değeri (Trim anahtarıyla); grup boş/karşılıksız → null.</summary>
    public static decimal? Deger(Dictionary<string, decimal> ortalamalar, string? grup)
        => !string.IsNullOrWhiteSpace(grup) && ortalamalar.TryGetValue(grup.Trim(), out var v) ? v : null;
}
