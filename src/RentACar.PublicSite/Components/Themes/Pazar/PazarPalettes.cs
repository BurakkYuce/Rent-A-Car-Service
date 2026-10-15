namespace RentACar.PublicSite.Components.Themes.Pazar;

/// <summary>
/// Pazar temasının hazır renk setleri — pazar tezgâhı ve dükkân tabelası renkleri. Her sette ana renk (tabela,
/// düz bloklar, birincil düğme) + vurgu (asılı fiyat etiketinin kartonu) ayrı; zeminler serin açık gri (krem DEĞİL),
/// koyu şemada sıcak olmayan koyu. Koyu şemanın ana rengi beyaz yazı taşıyacak kadar koyu seçildi (tabela hissi);
/// Hardal bilinçli istisna: koyu şemada hardal dolgu üstünde koyu mürekkep.
///
/// <para>İLK set varsayılan; elle ayarları tema-pazar.css'teki primitive'lerle BİREBİR aynı (yedekler = varsayılan
/// set, yapısal test). Anahtarlar KALICI ve <c>SitePaletteKeys</c> ile aynı olmalı (yapısal test).</para>
/// </summary>
public static class PazarPalettes
{
    public static readonly SitePalette Domates = new("domates", "Domates ve ayçiçeği",
        new SchemeColors("#edf0f2", "#ffffff", "#1b1d22", "#d42a1c", "#ffc928")
        {
            BgAlt = "#e1e5e9", SurfaceRaised = "#ffffff", InkMuted = "#454a53", InkSubtle = "#5a5f69",
            Line = "#c8cdd3", LineStrong = "#8b919a", AccentInk = "#1b1d22",
        },
        new SchemeColors("#16171a", "#1f2125", "#eef0f2", "#d13a28", "#ffc928")
        {
            BgAlt = "#1c1d21", SurfaceRaised = "#27292d", InkMuted = "#bcc1c8", InkSubtle = "#9ca2ab",
            Line = "#32353b", LineStrong = "#50545c", AccentInk = "#16171a",
        });

    public static readonly SitePalette Kobalt = new("kobalt", "Kobalt ve portakal",
        new SchemeColors("#eef1f6", "#ffffff", "#141a2b", "#1d4ed8", "#ff8a3d"),
        new SchemeColors("#11141c", "#1a1e29", "#eef1f6", "#3d6be6", "#ff8a3d"));

    /// <summary>Açıkta hardal dolgu zeminde 3:1'i tutmadığı için ana renk koyu hardal; parlak hardal etikette.
    /// Koyuda dolgu parlak hardal, üstünde koyu mürekkep (vurgu ana rengi izler).</summary>
    public static readonly SitePalette Hardal = new("hardal", "Hardal ve mürekkep",
        new SchemeColors("#eeefeb", "#ffffff", "#25220f", "#8a6800", "#f2b705"),
        new SchemeColors("#161510", "#211f17", "#f2f0e6", "#f2b705"));

    public static readonly SitePalette Cam = new("cam", "Çam yeşili ve kraft",
        new SchemeColors("#edf1ee", "#ffffff", "#13201a", "#1d6b46", "#f0d49b"),
        new SchemeColors("#0f1612", "#18211c", "#e9f0eb", "#23804f", "#f0d49b"));

    public static readonly SitePalette Patlican = new("patlican", "Patlıcan ve şeftali",
        new SchemeColors("#f1eff3", "#ffffff", "#1f1626", "#5d2a78", "#ffb38a"),
        new SchemeColors("#151119", "#1f1925", "#f1edf4", "#9156c8", "#ffb38a"));

    public static readonly IReadOnlyList<SitePalette> All = [Domates, Kobalt, Hardal, Cam, Patlican];
}
