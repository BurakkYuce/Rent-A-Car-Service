namespace RentACar.PublicSite.Components.Themes.Vitrin;

/// <summary>
/// Vitrin temasının hazır renk setleri. Tema KOYU ÖNCELİKLİ: ilk dört set koyu kimliklidir
/// (<see cref="SitePalette.DarkInLightMode"/>) — açık tercihli ziyaretçi de gece showroom'u görür; açık şemaları koyu
/// şemanın bir ton yumuşağıdır. Açık zemin isteyen firma için tek set var: Şampanya (açık şeması açık, koyu şeması
/// sıcak grafit).
///
/// <para><b>Renk kuralı:</b> sitedeki TEK kroma ana renktir ve DÜŞÜK doygunlukludur (platin, buz mavisi, gül,
/// adaçayı, bronz) — koyu zemin üstünde parlak tek bir asit vurgu YOK. Vurgu (Accent) tanımlanmaz, ana rengi izler;
/// firma kendi ana rengini girerse o da onu izler. Zeminler saf siyah değil: grafit, gece laciverti, bordo-siyah,
/// orman-siyah — fotoğraf onların üstünde parlar. Alan çerçevesi (<c>LineStrong</c>) zemin ve kartla 3:1 (WCAG 1.4.11)
/// olacak şekilde elle verilir; geri kalan tonlar üreticiden.</para>
///
/// <para>Anahtarlar KALICI: <c>grafit</c>, <c>gece-mavisi</c>, <c>bordo</c> palet altyapısından (PR #387) beri
/// kayıtlı olabilir, adları ve renkleri yenilendi ama anahtarları değişmedi.</para>
/// </summary>
public static class VitrinPalettes
{
    public static readonly SitePalette Grafit = new("grafit", "Grafit",
        new SchemeColors("#1c1f23", "#24282d", "#f1efea", "#d6cfc2") { LineStrong = "#74787d" },
        new SchemeColors("#121417", "#1a1d21", "#ecebe6", "#d6cfc2") { LineStrong = "#6a6e73" })
    {
        DarkInLightMode = true,
    };

    public static readonly SitePalette GeceMavisi = new("gece-mavisi", "Gece mavisi",
        new SchemeColors("#172030", "#1f2a3d", "#eef2f8", "#b9cbe6") { LineStrong = "#6c7a90" },
        new SchemeColors("#0e1520", "#151e2c", "#e8edf5", "#b9cbe6") { LineStrong = "#63718a" })
    {
        DarkInLightMode = true,
    };

    public static readonly SitePalette Bordo = new("bordo", "Bordo",
        new SchemeColors("#2a161b", "#351d23", "#f6ecec", "#e6b8b0") { LineStrong = "#8a6c72" },
        new SchemeColors("#1b0e12", "#26161b", "#f2e8e9", "#e6b8b0") { LineStrong = "#83666c" })
    {
        DarkInLightMode = true,
    };

    public static readonly SitePalette Orman = new("orman", "Orman",
        new SchemeColors("#17221d", "#1f2c26", "#edf2ee", "#c5d4b5") { LineStrong = "#6b7c72" },
        new SchemeColors("#0e1612", "#16211b", "#e7eee9", "#c5d4b5") { LineStrong = "#617268" })
    {
        DarkInLightMode = true,
    };

    public static readonly SitePalette Sampanya = new("sampanya", "Şampanya, açık zemin",
        new SchemeColors("#efede8", "#fbfaf7", "#1d1c1a", "#6e5a36") { LineStrong = "#8a8479" },
        new SchemeColors("#191714", "#221f1b", "#f0ebe2", "#d9c49b") { LineStrong = "#736c61" });

    public static readonly IReadOnlyList<SitePalette> All = [Grafit, GeceMavisi, Bordo, Orman, Sampanya];
}
