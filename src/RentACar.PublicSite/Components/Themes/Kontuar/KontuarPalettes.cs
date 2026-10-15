namespace RentACar.PublicSite.Components.Themes.Kontuar;

/// <summary>
/// Kontuar temasının hazır renk setleri. Ana renk birincil düğme, vurgu pano/kupon İŞARETİ (metin rengi
/// olarak asla). Kalkış panosu iki modda da açık şemanın mürekkebi üstünde koyu şemanın mürekkebiyle yazılır
/// (<c>--day-ink</c> / <c>--night-ink</c>). İLK set varsayılan ve bugünkü görünümü BİREBİR korur (elle ayar
/// değerleri tema-kontuar.css'teki primitive'lerle aynı). Diğer iki set geçici: asıl setleri tema yenilemesi yazar.
/// </summary>
public static class KontuarPalettes
{
    public static readonly SitePalette Terminal = new("terminal", "Terminal ve yer ekibi turuncusu",
        new SchemeColors("#f4f7f9", "#ffffff", "#16263a", "#ff6a13", "#ff6a13")
        {
            BgAlt = "#e7edf2", SurfaceRaised = "#ffffff", InkMuted = "#3b4d63", InkSubtle = "#52647a",
            Line = "#cfd8e0", LineStrong = "#8a98a7", AccentInk = "#16263a",
        },
        new SchemeColors("#0a131d", "#131d2b", "#e9eff5", "#ff6a13", "#ff6a13")
        {
            BgAlt = "#0f1925", SurfaceRaised = "#1a2738", InkMuted = "#a9b8c8", InkSubtle = "#a9b8c8",
            Line = "#24344a", LineStrong = "#3a5070", AccentInk = "#0a131d",
        });

    public static readonly SitePalette PistYesili = new("pist-yesili", "Pist yeşili ve sinyal sarısı",
        new SchemeColors("#f3f7f5", "#ffffff", "#12261f", "#0f7a55", "#f5b800"),
        new SchemeColors("#08140f", "#0f1e18", "#e6f1ec", "#0f7a55", "#f5b800"));

    public static readonly SitePalette Gok = new("gok", "Gök mavisi ve mercan",
        new SchemeColors("#f3f6fa", "#ffffff", "#14223a", "#1f6fd1", "#ff5a4e"),
        new SchemeColors("#0a1220", "#111b2c", "#e8eef7", "#1f6fd1", "#ff5a4e"));

    public static readonly IReadOnlyList<SitePalette> All = [Terminal, PistYesili, Gok];
}
