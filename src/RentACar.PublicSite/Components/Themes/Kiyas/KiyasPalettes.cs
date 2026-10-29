namespace RentACar.PublicSite.Components.Themes.Kiyas;

/// <summary>
/// Kıyas temasının hazır renk setleri. Kural: serin açık gri zemin, beyaz kartlar, firma rengi yalnız eylem/bağlantı;
/// vurgu yalnız işaret ("Baktığınız araç", toplam fiyatın alt çizgisi). Olumlu/dikkat rozetlerinin renkleri palete
/// bağlı DEĞİL (tema css'inde sabit, AA ayrıca test edilir). İLK set varsayılan; tema-kiyas.css'teki palet yedekleri
/// onunla BİREBİR aynı (yapısal test).
/// </summary>
public static class KiyasPalettes
{
    /// <summary>Kobalt mavi + amber, serin gri zemin. Varsayılan.</summary>
    public static readonly SitePalette Mavi = new("mavi", "Kobalt mavi",
        new SchemeColors("#f4f6f8", "#ffffff", "#14171c", "#1d4ed8", "#fbbf24") { InkMuted = "#566070" },
        new SchemeColors("#0d1117", "#161b22", "#e6edf3", "#79a6ff", "#fbbf24"));

    /// <summary>Turkuaz + turuncu (kıyı, havalimanı).</summary>
    public static readonly SitePalette Turkuaz = new("turkuaz", "Turkuaz ve turuncu",
        new SchemeColors("#f2f6f6", "#ffffff", "#10201f", "#0f766e", "#f59e0b"),
        new SchemeColors("#0b1414", "#122020", "#e2eeec", "#5fd0c4", "#f59e0b"));

    /// <summary>Kiremit + sarı.</summary>
    public static readonly SitePalette Kiremit = new("kiremit", "Kiremit ve sarı",
        new SchemeColors("#f7f5f2", "#ffffff", "#1c1917", "#c2410c", "#fcd34d"),
        new SchemeColors("#141210", "#1d1a17", "#f0ebe5", "#fb923c", "#fcd34d"));

    /// <summary>Gece mavisi + camgöbeği.</summary>
    public static readonly SitePalette Gece = new("gece", "Gece mavisi",
        new SchemeColors("#f3f4f7", "#ffffff", "#111827", "#312e81", "#22d3ee"),
        new SchemeColors("#0b0d14", "#131624", "#e5e7f0", "#a5b4fc", "#22d3ee"));

    public static readonly IReadOnlyList<SitePalette> All = [Mavi, Turkuaz, Kiremit, Gece];
}
