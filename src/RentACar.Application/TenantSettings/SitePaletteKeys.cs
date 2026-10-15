using System.Text.RegularExpressions;

namespace RentACar.Application.TenantSettings;

/// <summary>
/// ERP'de gösterilen palet seti kartı: ad + renk çipleri (açık şemanın zemini, mürekkebi, ana rengi, vurgusu ve
/// koyu şemanın zemini). Değerler PublicSite'taki setle BİREBİR aynı olmalı (yapısal test).
/// </summary>
/// <param name="Key">Kalıcı anahtar (<c>TenantSettings.SitePaleti</c>).</param>
/// <param name="Ad">Görünen ad.</param>
/// <param name="Zemin">Açık şema zemini.</param>
/// <param name="Murekkep">Açık şema metin rengi.</param>
/// <param name="Ana">Açık şema ana rengi.</param>
/// <param name="Vurgu">Açık şema vurgusu; null = ana rengi izler.</param>
/// <param name="GeceZemin">Koyu şema zemini.</param>
public sealed record SitePaletteOption(string Key, string Ad, string Zemin, string Murekkep, string Ana, string? Vurgu,
    string GeceZemin);

/// <summary>
/// Halka açık sitenin palet setleri: tema → izinli set anahtarları (ilk = varsayılan). Setlerin TAMAMI
/// (şema renkleri, elle ayarlar) PublicSite tema kaydındadır (<c>SiteTheme.Palettes</c>); bu liste ERP ve API'nin
/// ihtiyacı kadarını taşır: aitlik doğrulaması (<c>/ayarlar</c> PUT → <c>errors.sitePaleti</c>) ve seçim kartları.
/// İki liste yapısal testle birebir kilitli — yeni set iki yere de eklenir.
///
/// <para>Biçim kuralı (<see cref="Normalize"/>) servis katmanında; anahtar DB'de, sitenin <c>?palet=</c>
/// parametresinde ve CSS'e ASLA basılmadan kayıt aramasında kullanılır.</para>
/// </summary>
public static partial class SitePaletteKeys
{
    public const int MaxLength = 30;

    [GeneratedRegex("^[a-z0-9-]{1,30}$")]
    private static partial Regex KeyFormat();

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<SitePaletteOption>> ByTheme =
        new Dictionary<string, IReadOnlyList<SitePaletteOption>>(StringComparer.Ordinal)
        {
            [SiteThemeKeys.Pazar] =
            [
                new("petrol", "Petrol ve yol sarısı", "#f5f6f3", "#1d2227", "#0b5d6b", "#f2c230", "#121417"),
                new("lacivert", "Lacivert ve amber", "#f4f6f9", "#17202e", "#1f4e9c", "#f0a020", "#10151d"),
                new("zeytin", "Zeytin ve hardal", "#f4f5f0", "#222519", "#4e6b1f", "#e0b324", "#12140f"),
            ],
            [SiteThemeKeys.Vitrin] =
            [
                // Koyu öncelikli: ilk dört setin açık şeması da koyu (Zemin = açık şemanın zemini, burada koyu).
                new("grafit", "Grafit", "#1c1f23", "#f1efea", "#d6cfc2", null, "#121417"),
                new("gece-mavisi", "Gece mavisi", "#172030", "#eef2f8", "#b9cbe6", null, "#0e1520"),
                new("bordo", "Bordo", "#2a161b", "#f6ecec", "#e6b8b0", null, "#1b0e12"),
                new("orman", "Orman", "#17221d", "#edf2ee", "#c5d4b5", null, "#0e1612"),
                new("sampanya", "Şampanya, açık zemin", "#efede8", "#1d1c1a", "#6e5a36", null, "#191714"),
            ],
            [SiteThemeKeys.Rota] =
            [
                new("terminal", "Terminal ve yer ekibi turuncusu", "#f4f7f9", "#16263a", "#ff6a13", "#ff6a13", "#0a131d"),
                new("pist-yesili", "Pist yeşili ve sinyal sarısı", "#f3f7f5", "#12261f", "#0f7a55", "#f5b800", "#08140f"),
                new("gok", "Gök mavisi ve mercan", "#f3f6fa", "#14223a", "#1f6fd1", "#ff5a4e", "#0a1220"),
            ],
        };

    /// <summary>Kanonik anahtar (kırpılmış, küçük harf) ya da null (boş / biçimsiz). Temaya aitliğe BAKMAZ.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim().ToLowerInvariant();
        return KeyFormat().IsMatch(v) ? v : null;
    }

    /// <summary>Temanın setleri; boş/bilinmeyen tema → varsayılan temanınkiler.</summary>
    public static IReadOnlyList<SitePaletteOption> For(string? theme)
        => ByTheme[SiteThemeKeys.Normalize(theme) ?? SiteThemeKeys.Default];

    /// <summary>Anahtar (biçimli) bu temanın setlerinden biri mi.</summary>
    public static bool BelongsTo(string? theme, string? palette)
        => Normalize(palette) is { } k && For(theme).Any(p => p.Key == k);
}
