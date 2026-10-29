namespace RentACar.PublicSite.Components.Themes.Paket;

/// <summary>
/// Paket temasının hazır renk setleri. Kural: sıcak açık gri kâğıt zemin, tek vurgu = firma rengi (vurgu ana rengi
/// izler); koyu bant (kurumsal bölüm) açık şemanın mürekkebi (<c>--day-ink</c>) üstünde zemin rengi yazı. İLK set
/// varsayılan; tema-paket.css'teki palet yedekleri onunla BİREBİR aynı (yapısal test).
/// </summary>
public static class PaketPalettes
{
    /// <summary>Koyu kiremit, kâğıt zemin. Varsayılan.</summary>
    public static readonly SitePalette Kiremit = new("kiremit", "Koyu kiremit",
        new SchemeColors("#f3f2ee", "#ffffff", "#111316", "#8c2f1b") { InkMuted = "#5e5f63", Line = "#d9d7d0" },
        new SchemeColors("#121314", "#1b1c1e", "#ecebe7", "#e08b6f"));

    /// <summary>Lacivert (kurumsal).</summary>
    public static readonly SitePalette Lacivert = new("lacivert", "Lacivert",
        new SchemeColors("#f2f3f5", "#ffffff", "#0f1420", "#1e3a6e"),
        new SchemeColors("#0e1117", "#171b23", "#e8ebf1", "#8fb0ec"));

    /// <summary>Zeytin.</summary>
    public static readonly SitePalette Zeytin = new("zeytin", "Zeytin",
        new SchemeColors("#f2f2ec", "#ffffff", "#14160f", "#4a5a23"),
        new SchemeColors("#11120e", "#1a1b16", "#eceee4", "#b5c77f"));

    /// <summary>Grafit: renkli logoyla çakışmayan nötr set.</summary>
    public static readonly SitePalette Grafit = new("grafit", "Grafit",
        new SchemeColors("#f2f2f2", "#ffffff", "#15171a", "#2b2f36"),
        new SchemeColors("#111214", "#1a1b1e", "#ebecee", "#c3c7ce"));

    public static readonly IReadOnlyList<SitePalette> All = [Kiremit, Lacivert, Zeytin, Grafit];
}
