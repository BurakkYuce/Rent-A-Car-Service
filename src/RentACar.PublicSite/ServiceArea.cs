namespace RentACar.PublicSite;

/// <summary>
/// Firmanın hizmet bölgesi metni (yerel SEO: "Esenyurt, İstanbul bölgesinde araç kiralama"). Kaynak
/// aktif şubelerin il/ilçe alanları — firma adresi serbest metin olduğu için ayrıştırılmaz (sokak adı
/// şehir sanılırdı). Veri yoksa null → cümle hiç basılmaz (T2).
///
/// <para>Kural: tek il + tek ilçe → "İlçe, İl"; tek il, birden çok ilçe → "İl"; birden çok il → illerin
/// listesi (en fazla üç, ilk görülme sırasıyla). Ek ("'da/'de") KULLANILMAZ — ses uyumu yanlış ek
/// üretirdi; cümle "… bölgesinde" ile kurulur.</para>
/// </summary>
public static class ServiceArea
{
    private const int MaxCities = 3;

    public static string? Describe(IEnumerable<(string? City, string? District)> branches)
    {
        var rows = branches
            .Select(b => (City: b.City?.Trim() ?? "", District: b.District?.Trim() ?? ""))
            .Where(b => b.City.Length > 0)
            .ToList();
        if (rows.Count == 0) return null;

        var cities = rows.Select(r => r.City).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cities.Count > 1) return string.Join(", ", cities.Take(MaxCities));

        var districts = rows.Select(r => r.District).Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return districts.Count == 1 && rows.All(r => r.District.Length > 0)
            ? $"{districts[0]}, {cities[0]}"
            : cities[0];
    }
}
