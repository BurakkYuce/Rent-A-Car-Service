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
            [SiteThemeKeys.Anahtar] =
            [
                new("zumrut", "Zümrüt ve kehribar", "#fbf8f3", "#1e2329", "#0f6e5a", "#f2b33d", "#111614"),
                new("lacivert", "Lacivert ve kehribar", "#f7f8fa", "#19202e", "#1f4c99", "#f2b33d", "#0f131b"),
                new("visne", "Vişne ve hardal", "#fbf7f6", "#2a1e20", "#9e2a3a", "#e8b04a", "#171112"),
                new("grafit", "Grafit ve turuncu", "#f6f6f5", "#1d2024", "#2e3a46", "#f28c28", "#131518"),
            ],
            [SiteThemeKeys.Pazar] =
            [
                new("domates", "Domates ve ayçiçeği", "#edf0f2", "#1b1d22", "#d42a1c", "#ffc928", "#16171a"),
                new("kobalt", "Kobalt ve portakal", "#eef1f6", "#141a2b", "#1d4ed8", "#ff8a3d", "#11141c"),
                new("hardal", "Hardal ve mürekkep", "#eeefeb", "#25220f", "#8a6800", "#f2b705", "#161510"),
                new("cam", "Çam yeşili ve kraft", "#edf1ee", "#13201a", "#1d6b46", "#f0d49b", "#0f1612"),
                new("patlican", "Patlıcan ve şeftali", "#f1eff3", "#1f1626", "#5d2a78", "#ffb38a", "#151119"),
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
                new("deniz", "Deniz", "#f1f5f6", "#0e2a33", "#0b5e6b", "#e2b02a", "#0b171b"),
                new("zeytin", "Zeytin", "#f2f4f1", "#1c2617", "#4d6a27", "#7fb3d5", "#10140e"),
                new("gece-mavisi", "Gece mavisi", "#f2f4f8", "#121a2e", "#1f3f8f", "#3fbf9b", "#0b0f1a"),
                new("kayisi", "Kayısı", "#f3f4f6", "#1d2433", "#2a3446", "#f4a259", "#12161d"),
                new("tas", "Taş", "#f3f4f4", "#1e2326", "#4a555d", null, "#15191c"),
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
