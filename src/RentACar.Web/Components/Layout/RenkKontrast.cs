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
public static class RenkKontrast
{
    public const string Acik = "#ffffff";
    public const string Koyu = "#0f172a"; // slate-900 — temanın koyu metni

    public static string? UzerindekiMetin(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#') return null;
        if (!int.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            return null;

        var zemin = Parlaklik((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        var koyu = Parlaklik(0x0f, 0x17, 0x2a);
        var beyazaKarsi = 1.05 / (zemin + 0.05);
        var koyuyaKarsi = (zemin + 0.05) / (koyu + 0.05);
        return beyazaKarsi >= koyuyaKarsi ? Acik : Koyu;
    }

    /// <summary>WCAG 2 bağıl parlaklık (sRGB → doğrusal).</summary>
    private static double Parlaklik(int r, int g, int b)
    {
        static double Kanal(int c)
        {
            var s = c / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Kanal(r) + 0.7152 * Kanal(g) + 0.0722 * Kanal(b);
    }
}
