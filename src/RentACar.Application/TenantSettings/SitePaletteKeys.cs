using System.Text.RegularExpressions;

namespace RentACar.Application.TenantSettings;

/// <summary>
/// ERP'de gösterilen palet seti kartı: ad + renk çipleri (açık şemanın zemini, mürekkebi, ana rengi, vurgusu ve
/// koyu şemanın zemini). Değerler PublicSite'taki setle BİREBİR aynı olmalı (yapısal test).
/// </summary>
/// <param name="Key">Kalıcı anahtar (<c>TenantSettings.SitePaleti</c>).</param>
/// <param name="Name">Görünen ad.</param>
/// <param name="Background">Açık şema zemini.</param>
/// <param name="Ink">Açık şema metin rengi.</param>
/// <param name="Primary">Açık şema ana rengi.</param>
/// <param name="Accent">Açık şema vurgusu; null = ana rengi izler.</param>
/// <param name="NightBackground">Koyu şema zemini.</param>
public sealed record SitePaletteOption(string Key, string Name, string Background, string Ink, string Primary, string? Accent,
    string NightBackground);

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
            [SiteThemeKeys.Kiyas] =
            [
                new("mavi", "Kobalt mavi", "#f4f6f8", "#14171c", "#1d4ed8", "#fbbf24", "#0d1117"),
                new("turkuaz", "Turkuaz ve turuncu", "#f2f6f6", "#10201f", "#0f766e", "#f59e0b", "#0b1414"),
                new("kiremit", "Kiremit ve sarı", "#f7f5f2", "#1c1917", "#c2410c", "#fcd34d", "#141210"),
                new("gece", "Gece mavisi", "#f3f4f7", "#111827", "#312e81", "#22d3ee", "#0b0d14"),
            ],
            [SiteThemeKeys.Paket] =
            [
                new("kiremit", "Koyu kiremit", "#f3f2ee", "#111316", "#8c2f1b", null, "#121314"),
                new("lacivert", "Lacivert", "#f2f3f5", "#0f1420", "#1e3a6e", null, "#0e1117"),
                new("zeytin", "Zeytin", "#f2f2ec", "#14160f", "#4a5a23", null, "#11120e"),
                new("grafit", "Grafit", "#f2f2f2", "#15171a", "#2b2f36", null, "#111214"),
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
