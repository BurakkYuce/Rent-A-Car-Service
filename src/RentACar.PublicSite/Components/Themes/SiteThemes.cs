using RentACar.Application.TenantSettings;
using RentACar.PublicSite.Components.Themes.Pazar;
using RentACar.PublicSite.Components.Themes.Rota;
using RentACar.PublicSite.Components.Themes.Vitrin;

namespace RentACar.PublicSite.Components.Themes;

/// <summary>Temanın kendi barındırılan yazı tipi dosyası (woff2). App.razor @font-face'i PARMAK İZLİ adresle
/// (<c>@Assets</c>) basar — css içindeki düz <c>url()</c> parmak izi alamazdı ve önyüklenen dosya iki kez inerdi.</summary>
/// <param name="Family">font-family adı.</param>
/// <param name="Weight">font-weight (tek değer ya da "100 900" aralığı).</param>
/// <param name="File">wwwroot'a göre yol, ör. "fonts/onest-latin.woff2".</param>
/// <param name="UnicodeRange">unicode-range (latin ya da latin-ext alt kümesi).</param>
public sealed record ThemeFont(string Family, string Weight, string File, string UnicodeRange);

/// <summary>
/// Bir site teması. Sayfalar veri mantığını PAYLAŞIR; SLOTLAR temaya göre değişir
/// (<c>&lt;DynamicComponent Type=…&gt;</c>). Parametre imzaları SABİT — bkz. Components/Themes/README.md.
/// </summary>
/// <param name="Key">Kalıcı anahtar (<see cref="SiteThemeKeys"/>).</param>
/// <param name="Ad">ERP'de ve önizlemede görünen ad.</param>
/// <param name="Aciklama">Bir cümlelik açıklama (ERP radyo kartı).</param>
/// <param name="CssPath">wwwroot'a göre tema css'i, ör. "css/tema-pazar.css".</param>
/// <param name="FontPreloads">Önyüklenecek yazı tipi dosyaları (<see cref="Fonts"/>'taki File değerlerinden).</param>
/// <param name="Hero">Hero slotu bileşeni.</param>
/// <param name="Card">İlan kartı slotu bileşeni.</param>
/// <param name="Header">Üst bar slotu bileşeni.</param>
/// <param name="Footer">Alt bilgi slotu bileşeni.</param>
/// <param name="Home">Ana sayfa gövdesi slotu (<see cref="HomeModel"/>).</param>
/// <param name="Fleet">İlan koleksiyonu slotu (<see cref="FleetModel"/>).</param>
/// <param name="DetailHero">İlan detayının üst bölümü slotu (<see cref="DetailHeroModel"/>).</param>
public sealed record SiteTheme(string Key, string Ad, string Aciklama, string CssPath,
    IReadOnlyList<string> FontPreloads, Type Hero, Type Card, Type Header, Type Footer,
    Type Home, Type Fleet, Type DetailHero)
{
    /// <summary>Temanın @font-face listesi (latin + latin-ext). Boşsa tema sistem yazı tipi kullanır.</summary>
    public IReadOnlyList<ThemeFont> Fonts { get; init; } = [];

    /// <summary>Hazır renk setleri (en az bir). İLK set varsayılan: firma set seçmediyse ya da kayıtlı set bu temaya
    /// ait değilse o basılır. Tarayıcı çubuğu rengi (<c>theme-color</c>) de setin zemininden gelir.</summary>
    public required IReadOnlyList<SitePalette> Palettes { get; init; }

    /// <summary>Varsayılan set (<see cref="Palettes"/>'in ilki).</summary>
    public SitePalette DefaultPalette => Palettes[0];
}

/// <summary>Tema kaydı — tek kaynak. Yeni tema: klasör <c>Components/Themes/&lt;Ad&gt;/</c> + css + buraya kayıt +
/// <see cref="SiteThemeKeys"/>'e anahtar.</summary>
public static class SiteThemes
{
    /// <summary>Google Fonts / fontsource alt küme aralıkları. ₺ (U+20BA) latin-ext'te; ı (U+0131) latin'de.</summary>
    public const string Latin =
        "U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,"
        + "U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD";
    public const string LatinExt =
        "U+0100-02BA,U+02BD-02C5,U+02C7-02CC,U+02CE-02D7,U+02DD-02FF,U+0304,U+0308,U+0329,U+1D00-1DBF,"
        + "U+1E00-1E9F,U+1EF2-1EFF,U+2020,U+20A0-20AB,U+20AD-20C0,U+2113,U+2C60-2C7F,U+A720-A7FF";

    /// <summary>Pazar: Onest (gövde, değişken ağırlık 400–700, tek dosya) + Bricolage Grotesque başlık/fiyat (800,
    /// dar genişlik wdth 75, optik boyut 36 — fonttools instancer ile tek statik örnek; ayrı aile adıyla, tarayıcının
    /// font-stretch eşlemesine güvenilmez). Figtree aday olarak elendi: ₺ (U+20BA) glifi yok, gövdedeki fiyatlar
    /// yedek yazı tipine düşerdi. Lisanslar OFL-Onest.txt, OFL-BricolageGrotesque.txt (ayrılmış ad yok).
    /// Dört dosya ~72 KB; hepsi önyüklenir: ilk ekranda gövde Türkçe metni ve etiketteki ₺ iki alt kümeyi de ister.</summary>
    private static readonly IReadOnlyList<ThemeFont> PazarFonts =
    [
        new("Onest", "400 700", "fonts/onest-latin-ext.woff2", LatinExt),
        new("Onest", "400 700", "fonts/onest-latin.woff2", Latin),
        new("Bricolage Grotesque Condensed", "800", "fonts/bricolage-condensed-800-latin-ext.woff2", LatinExt),
        new("Bricolage Grotesque Condensed", "800", "fonts/bricolage-condensed-800-latin.woff2", Latin),
    ];

    /// <summary>Pazar (anahtar <c>pazar</c>, eski <c>tarife</c>'nin yerine): yerel, samimi, bütçe kiralaması. Firma
    /// rengi düz bloklar, kalın çerçeveli kartlar, asılı fiyat etiketleri; telefon ve WhatsApp öne çıkar.</summary>
    public static readonly SiteTheme Pazar = new(SiteThemeKeys.Pazar, "Pazar",
        "Kalın başlıklar, asılı fiyat etiketleri ve öne çıkan telefon ile WhatsApp; yerel firma ve bütçe kiralaması için.",
        "css/tema-pazar.css",
        [.. PazarFonts.Select(f => f.File)],
        typeof(PazarHero), typeof(PazarCard), typeof(PazarHeader), typeof(PazarFooter),
        typeof(PazarHome), typeof(PazarFleet), typeof(PazarDetailHero))
    {
        Fonts = PazarFonts,
        Palettes = PazarPalettes.All,
    };

    /// <summary>Archivo — gövde 400/600 normal genişlik, başlık/fiyat 700 Expanded (wdth 125). Google Fonts'un
    /// değişken dosyası (alt küme başına ~88 KB) fonttools instancer ile statik örneklere indirildi: altı dosya
    /// toplam ~81 KB. Expanded ayrı aile adıyla: tarayıcının font-stretch eşlemesine güvenilmez.</summary>
    private static readonly IReadOnlyList<ThemeFont> ArchivoFonts =
    [
        new("Archivo", "400", "fonts/archivo-400-latin-ext.woff2", LatinExt),
        new("Archivo", "400", "fonts/archivo-400-latin.woff2", Latin),
        new("Archivo", "600", "fonts/archivo-600-latin-ext.woff2", LatinExt),
        new("Archivo", "600", "fonts/archivo-600-latin.woff2", Latin),
        new("Archivo Expanded", "700", "fonts/archivo-expanded-700-latin-ext.woff2", LatinExt),
        new("Archivo Expanded", "700", "fonts/archivo-expanded-700-latin.woff2", Latin),
    ];

    /// <summary>Önyükleme: ilk ekranda görünen gövde (400) ve başlık (700 Expanded) dosyaları — Türkçe metin iki
    /// alt kümeyi de indirtir. 600 (etiket, strong) önyüklenmez: ilk ekranda yalnız form etiketlerinde.</summary>
    public static readonly SiteTheme Vitrin = new(SiteThemeKeys.Vitrin, "Vitrin",
        "Fotoğraf öne çıkar: büyük kapak görseli ve sade kartlar. Özenli fotoğraflı, üst segment filo için.",
        "css/tema-vitrin.css",
        [.. ArchivoFonts.Where(f => f.Weight != "600").Select(f => f.File)],
        typeof(VitrinHero), typeof(VitrinCard), typeof(VitrinHeader), typeof(VitrinFooter),
        typeof(VitrinHome), typeof(VitrinFleet), typeof(VitrinDetailHero))
    {
        Fonts = ArchivoFonts,
        Palettes = VitrinPalettes.All,
    };

    /// <summary>Literata (başlık, fiyat: ağırlık 500'de sabit, optik boyut ekseni 12–72 korunur — büyük başlıkta
    /// ince, liste satırında sağlam çizer) + Public Sans (gövde, değişken ağırlık 400–600). Google Fonts'un değişken
    /// dosyalarından fonttools instancer + alt küme: dört dosya toplam ~103 KB. Literata'nın @font-face ağırlığı
    /// "400 700" bildirilir: dosya tek ağırlık olduğu halde 600 isteyen ortak kurallarda (base.css) tarayıcı
    /// sahte kalın ÜRETMEZ, aynı dosyayı kullanır. ₺ (U+20BA) iki ailede de var (latin-ext). OFL-Literata.txt,
    /// OFL-PublicSans.txt.</summary>
    private static readonly IReadOnlyList<ThemeFont> RotaFonts =
    [
        new("Literata", "400 700", "fonts/literata-latin-ext.woff2", LatinExt),
        new("Literata", "400 700", "fonts/literata-latin.woff2", Latin),
        new("Public Sans", "400 600", "fonts/public-sans-latin-ext.woff2", LatinExt),
        new("Public Sans", "400 600", "fonts/public-sans-latin.woff2", Latin),
    ];

    /// <summary>Rota (eski <c>kontuar</c>'ın yerine; anahtar <c>rota</c>): tatil bölgesi, havalimanı/otel teslimi,
    /// turist ve aylık kiralayan. Editoryal ızgara, dikey fotoğraf, numaralı fiyat listesi. Önyükleme: dört dosya da
    /// ilk ekranda (Türkçe metin iki alt kümeyi de indirtir; başlık ve gövde birlikte görünür).</summary>
    public static readonly SiteTheme Rota = new(SiteThemeKeys.Rota, "Rota",
        "Editoryal ve ferah: büyük başlık, dikey fotoğraf ve numaralı fiyat listesi. Tatil bölgesinde, havalimanı ve otel teslimi yapan firmalar için.",
        "css/tema-rota.css",
        [.. RotaFonts.Select(f => f.File)],
        typeof(RotaHero), typeof(RotaCard), typeof(RotaHeader), typeof(RotaFooter),
        typeof(RotaHome), typeof(RotaFleet), typeof(RotaDetailHero))
    {
        Fonts = RotaFonts,
        Palettes = RotaPalettes.All,
    };

    public static readonly IReadOnlyList<SiteTheme> All = [Pazar, Vitrin, Rota];

    /// <summary>Anahtardan tema; eski anahtar yeni karşılığına (<see cref="SiteThemeKeys.LegacyAliases"/>),
    /// boş/bilinmeyen → varsayılan (<see cref="SiteThemeKeys.Default"/>).</summary>
    public static SiteTheme Resolve(string? key)
        => All.First(t => t.Key == (SiteThemeKeys.Normalize(key) ?? SiteThemeKeys.Default));

    /// <summary>Önizleme parametresi geçerli bir tema adı mı — eski anahtar da geçerli (geçersiz ad yok sayılır).</summary>
    public static bool IsKnown(string? key) => SiteThemeKeys.Normalize(key) is not null;
}
