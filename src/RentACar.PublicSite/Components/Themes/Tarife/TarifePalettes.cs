namespace RentACar.PublicSite.Components.Themes.Tarife;

/// <summary>
/// Tarife temasının hazır renk setleri. İLK set varsayılan ve bugünkü görünümü BİREBİR korur (elle ayar
/// değerleri tema-tarife.css'teki primitive'lerle aynı). Diğer iki set geçici: asıl setleri tema yenilemesi yazar.
/// Anahtarlar <c>SitePaletteKeys</c> ile aynı olmalı (yapısal test).
/// </summary>
public static class TarifePalettes
{
    public static readonly SitePalette Petrol = new("petrol", "Petrol ve yol sarısı",
        new SchemeColors("#f5f6f3", "#ffffff", "#1d2227", "#0b5d6b", "#f2c230")
        {
            BgAlt = "#eceee9", SurfaceRaised = "#ffffff", InkMuted = "#4f575e", InkSubtle = "#636b72",
            Line = "#dadfd8", LineStrong = "#b4bab2", AccentInk = "#1d2227",
        },
        new SchemeColors("#121417", "#1b1f23", "#eceeea", "#0b5d6b", "#f2c230")
        {
            BgAlt = "#22272c", SurfaceRaised = "#22272c", InkMuted = "#b6bdc2", InkSubtle = "#9aa2a9",
            Line = "#2c3238", LineStrong = "#474e55", AccentInk = "#121417",
        });

    public static readonly SitePalette Lacivert = new("lacivert", "Lacivert ve amber",
        new SchemeColors("#f4f6f9", "#ffffff", "#17202e", "#1f4e9c", "#f0a020"),
        new SchemeColors("#10151d", "#182029", "#e8edf3", "#1f4e9c", "#f0a020"));

    public static readonly SitePalette Zeytin = new("zeytin", "Zeytin ve hardal",
        new SchemeColors("#f4f5f0", "#ffffff", "#222519", "#4e6b1f", "#e0b324"),
        new SchemeColors("#12140f", "#1a1d16", "#eceee6", "#4e6b1f", "#e0b324"));

    public static readonly IReadOnlyList<SitePalette> All = [Petrol, Lacivert, Zeytin];
}
