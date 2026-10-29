namespace RentACar.PublicSite.Components.Themes.Anahtar;

/// <summary>
/// Anahtar temasının hazır renk setleri. Temanın kuralı: zemin açık ve sakin, firma rengi YALNIZ düğme, bağlantı ve
/// küçük işaretlerde (büyük renk bloğu yok); vurgu yalnız rozet zemini ("Baktığınız araç", Google puanı) ve üstünde
/// koyu yazı. WhatsApp düğmesinin yeşili palete bağlı DEĞİL (tema css'inde sabit, beyaz yazı 5,4:1).
/// İLK set varsayılan; tema-anahtar.css'teki palet yedekleri onunla BİREBİR aynı (yapısal test).
/// </summary>
public static class AnahtarPalettes
{
    /// <summary>Zümrüt yeşili + kehribar, kâğıt beyazı zemin. Varsayılan.</summary>
    public static readonly SitePalette Zumrut = new("zumrut", "Zümrüt ve kehribar",
        new SchemeColors("#fbf8f3", "#ffffff", "#1e2329", "#0f6e5a", "#f2b33d") { InkMuted = "#5b6470" },
        new SchemeColors("#111614", "#19201d", "#e9eeeb", "#4fbf9f", "#f2b33d"));

    /// <summary>Lacivert + kehribar.</summary>
    public static readonly SitePalette Lacivert = new("lacivert", "Lacivert ve kehribar",
        new SchemeColors("#f7f8fa", "#ffffff", "#19202e", "#1f4c99", "#f2b33d"),
        new SchemeColors("#0f131b", "#171c27", "#e7ebf2", "#8fb0ec", "#f2b33d"));

    /// <summary>Vişne + hardal.</summary>
    public static readonly SitePalette Visne = new("visne", "Vişne ve hardal",
        new SchemeColors("#fbf7f6", "#ffffff", "#2a1e20", "#9e2a3a", "#e8b04a"),
        new SchemeColors("#171112", "#201819", "#f1e9ea", "#ef8f9b", "#e8b04a"));

    /// <summary>Grafit + turuncu: renkli logoyla çakışmayan nötr set.</summary>
    public static readonly SitePalette Grafit = new("grafit", "Grafit ve turuncu",
        new SchemeColors("#f6f6f5", "#ffffff", "#1d2024", "#2e3a46", "#f28c28"),
        new SchemeColors("#131518", "#1b1e22", "#eceef0", "#b9c4cf", "#f28c28"));

    public static readonly IReadOnlyList<SitePalette> All = [Zumrut, Lacivert, Visne, Grafit];
}
