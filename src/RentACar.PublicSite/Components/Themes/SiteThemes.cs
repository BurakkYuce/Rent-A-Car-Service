using RentACar.Application.TenantSettings;
using RentACar.PublicSite.Components.Themes.Tarife;

namespace RentACar.PublicSite.Components.Themes;

/// <summary>Temanın kendi barındırılan yazı tipi dosyası (woff2). App.razor @font-face'i PARMAK İZLİ adresle
/// (<c>@Assets</c>) basar — css içindeki düz <c>url()</c> parmak izi alamazdı ve önyüklenen dosya iki kez inerdi.</summary>
/// <param name="Family">font-family adı.</param>
/// <param name="Weight">font-weight (tek değer ya da "100 900" aralığı).</param>
/// <param name="File">wwwroot'a göre yol, ör. "fonts/barlow-400-latin.woff2".</param>
/// <param name="UnicodeRange">unicode-range (latin ya da latin-ext alt kümesi).</param>
public sealed record ThemeFont(string Family, string Weight, string File, string UnicodeRange);

/// <summary>
/// Bir site teması. Sayfalar veri mantığını PAYLAŞIR; yalnız dört SLOT temaya göre değişir
/// (<c>&lt;DynamicComponent Type=…&gt;</c>). Parametre imzaları SABİT — bkz. Components/Themes/README.md.
/// </summary>
/// <param name="Key">Kalıcı anahtar (<see cref="SiteThemeKeys"/>).</param>
/// <param name="Ad">ERP'de ve önizlemede görünen ad.</param>
/// <param name="Aciklama">Bir cümlelik açıklama (ERP radyo kartı).</param>
/// <param name="CssPath">wwwroot'a göre tema css'i, ör. "css/tema-tarife.css".</param>
/// <param name="FontPreloads">Önyüklenecek yazı tipi dosyaları (<see cref="Fonts"/>'taki File değerlerinden).</param>
/// <param name="Hero">Hero slotu bileşeni.</param>
/// <param name="Card">İlan kartı slotu bileşeni.</param>
/// <param name="Header">Üst bar slotu bileşeni.</param>
/// <param name="Footer">Alt bilgi slotu bileşeni.</param>
public sealed record SiteTheme(string Key, string Ad, string Aciklama, string CssPath,
    IReadOnlyList<string> FontPreloads, Type Hero, Type Card, Type Header, Type Footer)
{
    /// <summary>Temanın @font-face listesi (latin + latin-ext). Boşsa tema sistem yazı tipi kullanır.</summary>
    public IReadOnlyList<ThemeFont> Fonts { get; init; } = [];

    /// <summary>Firma renk seçmediyse BrandPalette'e verilen varsayılan vurgu ("#rrggbb").</summary>
    public string DefaultBrand { get; init; } = RentACar.PublicSite.BrandPalette.DefaultColor;

    /// <summary>Tarayıcı çubuğu rengi (açık / koyu) — tema zeminiyle aynı.</summary>
    public string ThemeColorLight { get; init; } = "#f5f6f3";
    public string ThemeColorDark { get; init; } = "#121417";
}

/// <summary>Tema kaydı — tek kaynak. Yeni tema: klasör <c>Components/Themes/&lt;Ad&gt;/</c> + css + buraya kayıt +
/// <see cref="SiteThemeKeys"/>'e anahtar.</summary>
public static class SiteThemes
{
    /// <summary>Barlow alt küme aralıkları (fontsource ile aynı). ₺ (U+20BA) latin-ext'te.</summary>
    public const string Latin =
        "U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,"
        + "U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD";
    public const string LatinExt =
        "U+0100-02BA,U+02BD-02C5,U+02C7-02CC,U+02CE-02D7,U+02DD-02FF,U+0304,U+0308,U+0329,U+1D00-1DBF,"
        + "U+1E00-1E9F,U+1EF2-1EFF,U+2020,U+20A0-20AB,U+20AD-20C0,U+2113,U+2C60-2C7F,U+A720-A7FF";

    private static readonly IReadOnlyList<ThemeFont> BarlowFonts =
    [
        new("Barlow", "400", "fonts/barlow-400-latin-ext.woff2", LatinExt),
        new("Barlow", "400", "fonts/barlow-400-latin.woff2", Latin),
        new("Barlow Condensed", "600", "fonts/barlow-condensed-600-latin-ext.woff2", LatinExt),
        new("Barlow Condensed", "600", "fonts/barlow-condensed-600-latin.woff2", Latin),
    ];

    public static readonly SiteTheme Tarife = new(SiteThemeKeys.Tarife, "Tarife",
        "Fiyat şeffaflığı öne çıkar: günlük fiyat tahtası ve açık hesap. Bütçe ve şehir kiralaması için.",
        "css/tema-tarife.css",
        [.. BarlowFonts.Select(f => f.File)],
        typeof(TarifeHero), typeof(TarifeCard), typeof(TarifeHeader), typeof(TarifeFooter))
    {
        Fonts = BarlowFonts,
    };

    // GEÇİCİ YER TUTUCU: vitrin ve kontuar temaları ayrı PR'larda yazılıyor. O PR'lar kendi slot
    // bileşenlerini, css'ini, yazı tiplerini ve renklerini buraya koyar. O zamana kadar tarife'nin
    // slotlarını kullanırlar; css dosyaları tarife'yi içe aktarır.
    public static readonly SiteTheme Vitrin = Tarife with
    {
        Key = SiteThemeKeys.Vitrin, Ad = "Vitrin",
        Aciklama = "Fotoğraf öne çıkar: büyük kapak görseli ve sade kartlar. Özenli fotoğraflı, üst segment filo için.",
        CssPath = "css/tema-vitrin.css",
    };

    public static readonly SiteTheme Kontuar = Tarife with
    {
        Key = SiteThemeKeys.Kontuar, Ad = "Kontuar",
        Aciklama = "Bilet ve kalkış panosu düzeni. Havalimanı ve otel teslimi yapan firmalar için.",
        CssPath = "css/tema-kontuar.css",
    };

    public static readonly IReadOnlyList<SiteTheme> All = [Tarife, Vitrin, Kontuar];

    /// <summary>Anahtardan tema; boş/bilinmeyen → varsayılan (tarife).</summary>
    public static SiteTheme Resolve(string? key)
        => SiteThemeKeys.Normalize(key) is { } k ? All.First(t => t.Key == k) : Tarife;

    /// <summary>Önizleme parametresi geçerli bir tema adı mı (geçersiz ad yok sayılır).</summary>
    public static bool IsKnown(string? key) => SiteThemeKeys.Normalize(key) is not null;
}
