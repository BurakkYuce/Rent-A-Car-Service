namespace RentACar.PublicSite.Components.Themes.Rota;

/// <summary>
/// Rota temasının hazır renk setleri. Temanın kuralı: zemin SERİN (tuz beyazı, buz grisi; krem/bej DEĞİL),
/// ana renk eylem ve bağlantı, vurgu yalnız işaret (dikey fotoğrafın arkasındaki renk levhası, "Baktığınız araç"
/// rozeti) — metin rengi olarak asla. İLK set varsayılan; elle ayar değerleri tema-rota.css'teki palet
/// yedekleriyle BİREBİR aynı (yapısal test).
///
/// <para><b>Kayısı neden ana renk değil:</b> açık kayısı, açık zeminde dolgu olarak 3:1'i tutmuyor; üretici onu
/// karamel/kiremit tonuna koyulaştırırdı (krem + serif + kiremit klişesine en yakın sonuç). Bu yüzden açık şemada
/// kayısı VURGU, eylem rengi koyu mürekkep ("koyu mürekkepli"); koyu şemada kayısı kendi parlaklığıyla ana renk,
/// üstündeki yazı koyu.</para>
/// </summary>
public static class RotaPalettes
{
    /// <summary>Petrol mavisi + şamandıra sarısı. Varsayılan.</summary>
    public static readonly SitePalette Deniz = new("deniz", "Deniz",
        new SchemeColors("#f1f5f6", "#ffffff", "#0e2a33", "#0b5e6b", "#e2b02a")
        {
            BgAlt = "#e5ecee", SurfaceRaised = "#ffffff", InkMuted = "#3a5560", InkSubtle = "#4d6872",
            Line = "#d2dde0", LineStrong = "#7a9099", AccentInk = "#0e2a33",
        },
        new SchemeColors("#0b171b", "#122429", "#e4eef0", "#5cb8c4", "#e2b02a")
        {
            BgAlt = "#0f1f24", SurfaceRaised = "#183037", InkMuted = "#a9bec4", InkSubtle = "#9ab1b8",
            Line = "#24414a", LineStrong = "#557580", AccentInk = "#0b171b",
        });

    /// <summary>Zeytin yeşili + gök mavisi (Ege kıyısı).</summary>
    public static readonly SitePalette Zeytin = new("zeytin", "Zeytin",
        new SchemeColors("#f2f4f1", "#ffffff", "#1c2617", "#4d6a27", "#7fb3d5"),
        new SchemeColors("#10140e", "#181e15", "#e6ebe2", "#9cbf6a", "#7fb3d5"));

    /// <summary>Gece mavisi + nane (havalimanı, iş seyahati).</summary>
    public static readonly SitePalette GeceMavisi = new("gece-mavisi", "Gece mavisi",
        new SchemeColors("#f2f4f8", "#ffffff", "#121a2e", "#1f3f8f", "#3fbf9b"),
        new SchemeColors("#0b0f1a", "#121828", "#e5e9f2", "#8aa6f0", "#3fbf9b"));

    /// <summary>Kayısı + koyu mürekkep (bkz. sınıf açıklaması: açık şemada kayısı vurgu, koyu şemada ana renk).</summary>
    public static readonly SitePalette Kayisi = new("kayisi", "Kayısı",
        new SchemeColors("#f3f4f6", "#ffffff", "#1d2433", "#2a3446", "#f4a259"),
        new SchemeColors("#12161d", "#1a1f28", "#eceef2", "#f4a259", "#f4a259"));

    /// <summary>Taş grisi: tek renkli, vurgu ana rengi izler.</summary>
    public static readonly SitePalette Tas = new("tas", "Taş",
        new SchemeColors("#f3f4f4", "#ffffff", "#1e2326", "#4a555d"),
        new SchemeColors("#15191c", "#1d2226", "#e8eaeb", "#b4bec5"));

    public static readonly IReadOnlyList<SitePalette> All = [Deniz, Zeytin, GeceMavisi, Kayisi, Tas];
}
