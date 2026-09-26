using System.Globalization;

namespace RentACar.Web.Components.Layout;

/// <summary>
/// Tenant'ın serbest seçtiği bir zemin renginin ÜZERİNE yazılacak metin rengi (açık/koyu).
///
/// <para><b>Neden var (adversarial bulgu):</b> Panel'deki acil "Gecikmiş" çipi zemini tenant'ın
/// "Gecikenler" renginden alıp üstüne SABİT beyaz metin basıyordu. Renk, Ayarlar'daki serbest renk
/// seçiciden gelir ve yalnız <c>#rrggbb</c> biçimi doğrulanır; sarı (#fde047) seçen tenant'ta beyaz
/// metnin kontrastı ~1,3:1'e düşüp çip okunmaz oluyordu. Metin rengi artık zeminden türetilir.</para>
///
/// <para>Kural WCAG 2 bağıl parlaklığıdır: beyaz ve koyu metnin zemine karşı kontrast oranı
/// hesaplanır, YÜKSEK olan seçilir. Biçimsiz girdi için <c>null</c> (çağıran değişkeni hiç basmaz,
/// CSS'teki geri dönüş değeri geçerli olur).</para>
/// </summary>
public static class ColorContrast
{
    public const string Open = "#ffffff";
    public const string Dark = "#0f172a"; // slate-900 — temanın koyu metni

    public static string? TextOn(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#') return null;
        if (!int.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            return null;

        var background = Brightness((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        var dark = Brightness(0x0f, 0x17, 0x2a);
        var againstWhite = 1.05 / (background + 0.05);
        var againstDark = (background + 0.05) / (dark + 0.05);
        return againstWhite >= againstDark ? Open : Dark;
    }

    /// <summary>WCAG 2 bağıl parlaklık (sRGB → doğrusal).</summary>
    private static double Brightness(int r, int g, int b)
    {
        static double Channel(int c)
        {
            var s = c / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }
}
