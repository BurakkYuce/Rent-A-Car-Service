using System.Globalization;
using System.Text;

namespace RentACar.PublicSite;

/// <summary>
/// Firmanın vurgu renginden (<c>TenantSettings.SiteVurguRengi</c>) sitenin marka değişkenlerini üretir.
///
/// <para><b>Neden renk olduğu gibi basılmıyor:</b> firma rengi iki farklı işte kullanılıyor ve ikisinin
/// kontrast şartı farklı. (1) DOLGU — birincil düğme zemini: üstündeki yazı rengi (siyah/beyaz) burada
/// SEÇİLİR, renk değişmez; her renk için ikisinden biri en az 4.58:1 verir (WCAG AA metin 4.5:1).
/// (2) METİN — bağlantı ve fiyat vurgusu sayfa zemininde yazı olarak duruyor: sarı ya da açık yeşil
/// bir firma rengi açık zeminde okunmaz. Bu yüzden metin varyantı, zeminle 4.5:1'e ulaşana kadar
/// koyulaştırılır (koyu temada açıklaştırılır). Firmanın dolgu rengi korunur, okunabilirlik de.</para>
///
/// <para><b>Koyu tema dolgusu:</b> çok koyu bir firma rengi (lacivert) koyu zeminde düğmeyi görünmez
/// yapar. Dolgu, koyu zeminle en az 3:1 (WCAG 1.4.11 bileşen sınırı) olana kadar açıklaştırılır.</para>
///
/// <para><b>Enjeksiyon:</b> çıktı ham ayar metninden DEĞİL, çözümlenmiş RGB bileşenlerinden yazılır.
/// Biçimi bozuk bir değer (servis zaten reddediyor) sessizce varsayılana düşer — CSS'e serbest metin
/// hiçbir yoldan giremez.</para>
/// </summary>
public static class BrandPalette
{
    /// <summary>Firma renk seçmemişse kullanılan varsayılan (koyu petrol). site.css'teki geri dönüş
    /// değerleri bununla aynıdır; stil bloğu basılamazsa site yine aynı görünür.</summary>
    public const string DefaultColor = "#0b5d6b";

    /// <summary>Açık tema sayfa zemini (site.css <c>--paper</c>). Kontrast bu zemine göre ölçülür:
    /// beyaz kart zemininden koyu olduğu için burada geçen değer beyazda da geçer.</summary>
    public const string LightPaper = "#f5f6f3";

    /// <summary>Koyu tema sayfa zemini (site.css koyu <c>--paper</c>). Kart zemini (<c>--surface</c>) bundan
    /// açıktır; metin varyantı KART zeminine göre de ayrıca doğrulanır.</summary>
    public const string DarkPaper = "#121417";

    /// <summary>Koyu tema kart zemini (site.css koyu <c>--surface</c>).</summary>
    public const string DarkSurface = "#1b1f23";

    public const double TextContrast = 4.5;
    public const double UiContrast = 3.0;

    private const string White = "#ffffff";
    private const string Ink = "#1d2227";

    /// <summary>Tek tema için marka değişkenleri.</summary>
    /// <param name="Fill">Birincil düğme zemini.</param>
    /// <param name="OnFill">Dolgu üstündeki yazı rengi.</param>
    /// <param name="Text">Sayfa/kart zemininde yazı olarak kullanılan marka tonu.</param>
    /// <param name="Soft">Sayfa zeminine karışmış hafif ton (seçili satır, bilgi kutusu zemini).</param>
    public sealed record Scheme(string Fill, string OnFill, string Text, string Soft);

    public sealed record Palette(Scheme Light, Scheme Dark);

    /// <summary>"#rrggbb" değerinden palet; boş ya da biçimsiz değer varsayılan renge düşer.</summary>
    public static Palette For(string? hex)
    {
        var baseColor = Rgb.TryParse(hex, out var c) ? c : Rgb.Parse(DefaultColor);
        var lightPaper = Rgb.Parse(LightPaper);
        var darkPaper = Rgb.Parse(DarkPaper);
        var darkSurface = Rgb.Parse(DarkSurface);
        var white = Rgb.Parse(White);
        var black = new Rgb(0, 0, 0);

        // ---- Açık tema ----
        var lightFill = baseColor;
        var lightText = Adjust(baseColor, black, t => Contrast(t, lightPaper) >= TextContrast);
        var light = new Scheme(lightFill.Hex, OnColor(lightFill).Hex, lightText.Hex,
            Rgb.Mix(lightPaper, baseColor, 0.12).Hex);

        // ---- Koyu tema ----
        var darkFill = Adjust(baseColor, white, t => Contrast(t, darkPaper) >= UiContrast);
        var darkText = Adjust(baseColor, white,
            t => Contrast(t, darkPaper) >= TextContrast && Contrast(t, darkSurface) >= TextContrast);
        var dark = new Scheme(darkFill.Hex, OnColor(darkFill).Hex, darkText.Hex,
            Rgb.Mix(darkPaper, baseColor, 0.22).Hex);

        return new Palette(light, dark);
    }

    /// <summary>
    /// <c>&lt;style&gt;</c> bloğunun içeriği. Tema seçimi site.css ile AYNI kural (<c>prefers-color-scheme</c>);
    /// değişken adları site.css'tekilerle birebir (<c>--brand</c>, <c>--on-brand</c>, <c>--brand-text</c>,
    /// <c>--brand-soft</c>).
    /// </summary>
    public static string Css(string? hex)
    {
        var p = For(hex);
        var sb = new StringBuilder(260);
        sb.Append(":root{").Append(Vars(p.Light)).Append('}');
        sb.Append("@media (prefers-color-scheme:dark){:root{").Append(Vars(p.Dark)).Append("}}");
        return sb.ToString();
    }

    private static string Vars(Scheme s)
        => $"--brand:{s.Fill};--on-brand:{s.OnFill};--brand-text:{s.Text};--brand-soft:{s.Soft}";

    /// <summary>
    /// Dolgu üstündeki yazı. Önce beyaz ya da sitenin mürekkep rengi (kontrastı yüksek olan) denenir.
    /// Orta parlaklıktaki renklerde (ör. #3399cc) ikisi de 4.5:1'in altında kalabiliyor — mürekkep saf
    /// siyah DEĞİL. O durumda saf siyah/beyazdan yüksek olan seçilir: bu ikisinden biri HER renkte en az
    /// 4.58:1 verir (matematiksel alt sınır), bu yüzden sonuç daima AA'yı geçer.
    /// </summary>
    public static Rgb OnColor(Rgb fill)
    {
        var white = Rgb.Parse(White);
        var ink = Rgb.Parse(Ink);
        var cw = Contrast(white, fill);
        var ci = Contrast(ink, fill);
        if (cw >= TextContrast && cw >= ci) return white;
        if (ci >= TextContrast) return ink;
        var black = new Rgb(0, 0, 0);
        return cw >= Contrast(black, fill) ? white : black;
    }

    /// <summary>Rengi <paramref name="toward"/> yönüne %2'lik adımlarla karıştırır; koşulu sağlayan İLK
    /// (yani asıl renge en yakın) tonu döner. Koşul hiç sağlanmazsa hedef rengin kendisi (siyah/beyaz)
    /// döner — o da her iki zeminde en yüksek kontrastı verir.</summary>
    private static Rgb Adjust(Rgb color, Rgb toward, Func<Rgb, bool> ok)
    {
        for (var step = 0; step <= 50; step++)
        {
            var candidate = Rgb.Mix(color, toward, step / 50.0);
            if (ok(candidate)) return candidate;
        }
        return toward;
    }

    /// <summary>WCAG 2.x kontrast oranı (1..21).</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        var la = a.Luminance;
        var lb = b.Luminance;
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>8-bit sRGB renk.</summary>
    public readonly record struct Rgb(int R, int G, int B)
    {
        public string Hex => string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}");

        /// <summary>WCAG göreli parlaklığı.</summary>
        public double Luminance => 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);

        private static double Channel(int v)
        {
            var c = v / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        public static Rgb Mix(Rgb a, Rgb b, double t)
            => new(Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));

        private static int Lerp(int x, int y, double t)
            => (int)Math.Round(x + (y - x) * t, MidpointRounding.AwayFromZero);

        public static Rgb Parse(string hex)
            => TryParse(hex, out var c) ? c : throw new FormatException($"Geçersiz renk: {hex}");

        /// <summary>Yalnız KESİN "#rrggbb" (servis kuralıyla aynı); başka her şey false.</summary>
        public static bool TryParse(string? hex, out Rgb color)
        {
            color = default;
            if (hex is not { Length: 7 } || hex[0] != '#') return false;
            if (!int.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
                return false;
            color = new Rgb((v >> 16) & 0xff, (v >> 8) & 0xff, v & 0xff);
            return true;
        }
    }
}
