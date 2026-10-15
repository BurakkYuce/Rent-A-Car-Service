using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using RentACar.PublicSite.Components.Themes;

namespace RentACar.PublicSite;

/// <summary>
/// Görünüm ÖNİZLEMESİ sorgu parametreleri: <c>?tema=</c> (tema anahtarı), <c>?palet=</c> (palet seti
/// anahtarı), <c>?renk=</c> (özel ana renk, yalnız KESİN <c>#rrggbb</c> — adreste <c>%23rrggbb</c>). Kurallar
/// TEK yerde: ThemeAccessor (istek başına çözüm), NoIndexHeaderMiddleware (geçerli parametre → noindex),
/// NotFoundPageMiddleware (404 sayfasına taşıma) aynı ayrıştırıcıyı kullanır.
///
/// <para>Önizleme kalıcı değildir (çerez/depolama yok), kanonik adres sorgusuz olduğundan değişmez. GEÇERSİZ
/// değer yok sayılır: o parametre hiç verilmemiş gibi davranılır (kayıtlı ayar basılır, noindex yazılmaz).
/// Palet yalnız biçim + "herhangi bir temanın seti" olarak doğrulanır; seçili temaya ait değilse ThemeAccessor
/// onu ayrıca yok sayar. Değerler sayfaya ASLA ham basılmaz (renk Rgb'den yeniden yazılır).</para>
/// </summary>
public static class SitePreview
{
    public const string ThemeKey = "tema";
    public const string PaletteKey = "palet";
    public const string ColorKey = "renk";

    /// <summary>Geçerli (normalize edilmiş) önizleme değerleri; geçersiz/eksik olan null.</summary>
    public sealed record Values(string? Theme, string? Palette, string? Color)
    {
        public bool Any => Theme is not null || Palette is not null || Color is not null;
    }

    public static readonly Values None = new(null, null, null);

    public static Values Parse(QueryString query)
        => query.HasValue && query.Value != "?" ? Parse(QueryHelpers.ParseQuery(query.Value)) : None;

    public static Values Parse(IEnumerable<KeyValuePair<string, StringValues>> query)
    {
        string? theme = null, palette = null, color = null;
        foreach (var (key, values) in query)
        {
            // Aynı parametre birden çok kez verilmişse değer belirsiz → yok sayılır.
            if (values.Count != 1) continue;
            var v = values[0];
            if (key.Equals(ThemeKey, StringComparison.Ordinal) && SiteThemes.IsKnown(v))
                theme = SiteThemes.Resolve(v).Key;
            else if (key.Equals(PaletteKey, StringComparison.Ordinal) && KnownPalette(v) is { } p)
                palette = p;
            else if (key.Equals(ColorKey, StringComparison.Ordinal) && Rgb.TryParse(v, out var c))
                color = c.Hex;
        }
        return new Values(theme, palette, color);
    }

    /// <summary>Yalnız geçerli değerlerden kurulan sorgu (404 sayfasına taşınır); diğer parametreler düşer.</summary>
    public static QueryString ToQuery(Values v)
    {
        var q = QueryString.Empty;
        if (v.Theme is not null) q = q.Add(ThemeKey, v.Theme);
        if (v.Palette is not null) q = q.Add(PaletteKey, v.Palette);
        if (v.Color is not null) q = q.Add(ColorKey, v.Color);
        return q;
    }

    private static string? KnownPalette(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var k = value.Trim().ToLowerInvariant();
        return SiteThemes.All.Any(t => t.Palettes.Any(p => p.Key == k)) ? k : null;
    }
}
