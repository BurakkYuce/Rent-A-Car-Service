using System.Globalization;

namespace RentACar.PublicSite;

/// <summary>8-bit sRGB renk. Sitenin renk çıktısı YALNIZ bu tipten yazılır (<see cref="Hex"/>): ayar ya da
/// sorgu metni hiçbir yoldan CSS'e doğrudan girmez.</summary>
public readonly record struct Rgb(int R, int G, int B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);

    public string Hex => string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}");

    /// <summary>WCAG göreli parlaklığı.</summary>
    public double Luminance => 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);

    private static double Channel(int v)
    {
        var c = v / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>sRGB'de doğrusal karışım: <paramref name="t"/>=0 → a, 1 → b.</summary>
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
        // Her karakter AÇIKÇA onaltılık: int.TryParse sondaki NUL'u ('\0') yutuyordu.
        for (var i = 1; i < 7; i++)
            if (!char.IsAsciiHexDigit(hex[i])) return false;
        if (!int.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
            return false;
        color = new Rgb((v >> 16) & 0xff, (v >> 8) & 0xff, v & 0xff);
        return true;
    }

    /// <summary>WCAG 2.x kontrast oranı (1..21).</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        var la = a.Luminance;
        var lb = b.Luminance;
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
