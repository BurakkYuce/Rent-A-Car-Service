namespace RentACar.PublicSite.Components.Themes.Vitrin;

/// <summary>
/// Vitrin temasının hazır renk setleri. Temanın kuralı: sitedeki TEK kroma ana renktir — vurgu (Accent) yok,
/// ana rengi izler. İLK set varsayılan ve bugünkü görünümü BİREBİR korur (elle ayar değerleri tema-vitrin.css'teki
/// primitive'lerle aynı). Diğer iki set geçici: asıl setleri tema yenilemesi yazar.
/// </summary>
public static class VitrinPalettes
{
    public static readonly SitePalette Grafit = new("grafit", "Porselen ve grafit",
        new SchemeColors("#f7f7f5", "#ffffff", "#2a3036", "#0b5d6b")
        {
            BgAlt = "#eeefec", SurfaceRaised = "#ffffff", InkMuted = "#50575e", InkSubtle = "#62696f",
            Line = "#c5cbd1", LineStrong = "#858d95",
        },
        new SchemeColors("#14171a", "#1b1f23", "#eef0f1", "#0b5d6b")
        {
            BgAlt = "#1b1f23", SurfaceRaised = "#1b1f23", InkMuted = "#c9ced3", InkSubtle = "#a3aab1",
            Line = "#2a3036", LineStrong = "#62696f",
        });

    public static readonly SitePalette GeceMavisi = new("gece-mavisi", "Gece mavisi",
        new SchemeColors("#f5f6f8", "#ffffff", "#1c2433", "#23407a"),
        new SchemeColors("#0e131c", "#161c27", "#edf0f5", "#23407a"));

    public static readonly SitePalette Bordo = new("bordo", "Bordo",
        new SchemeColors("#f7f6f5", "#ffffff", "#2a2526", "#7b1e2e"),
        new SchemeColors("#161213", "#1f1a1b", "#f1edee", "#7b1e2e"));

    public static readonly IReadOnlyList<SitePalette> All = [Grafit, GeceMavisi, Bordo];
}
