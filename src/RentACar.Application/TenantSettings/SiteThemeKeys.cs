namespace RentACar.Application.TenantSettings;

/// <summary>
/// Halka açık sitenin seçilebilir temaları (<c>TenantSettings.SiteTemasi</c>). Anahtarlar KALICI: DB'de
/// saklanır ve sitenin `?tema=` önizleme parametresinde kullanılır. Yeni tema eklenirse hem buraya hem
/// PublicSite'ın <c>SiteThemes</c> kaydına eklenir (yapısal test ikisinin aynı olduğunu kilitler).
/// </summary>
public static class SiteThemeKeys
{
    public const string Tarife = "tarife";
    public const string Vitrin = "vitrin";
    public const string Kontuar = "kontuar";

    /// <summary>Seçilmemiş / bilinmeyen tema → bu.</summary>
    public const string Default = Tarife;

    public static readonly IReadOnlyList<string> All = [Tarife, Vitrin, Kontuar];

    /// <summary>Kanonik anahtar ya da null (tanınmayan/boş).</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim().ToLowerInvariant();
        return All.Contains(v) ? v : null;
    }
}
