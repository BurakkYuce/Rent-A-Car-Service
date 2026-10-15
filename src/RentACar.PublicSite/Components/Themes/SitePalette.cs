namespace RentACar.PublicSite.Components.Themes;

/// <summary>
/// Bir renk şemasının (açık ya da koyu) TEMEL renkleri, "#rrggbb". Geri kalan semantik token'lar (ikinci zemin,
/// ikincil/ipucu metni, çizgiler, firma rengi dolgu/metin/hafif ton, vurgu üstü yazı) <see cref="SitePaletteCss"/>
/// tarafından bunlardan TÜRETİLİR ve kontrast güvencesinden geçer.
///
/// <para><b>Elle ayar (isteğe bağlı):</b> init özellikleri türetilen tonun yerine tasarımcının seçtiği değeri
/// koyar (varsayılan setler bugünkü tema renklerini birebir korumak için kullanır). Elle verilen değer de AYNI
/// kontrast güvencesinden geçer: tutmayan değer sessizce düzeltilir, basılmaz.</para>
/// </summary>
/// <param name="Bg">Sayfa zemini.</param>
/// <param name="Surface">Kart/panel zemini.</param>
/// <param name="Ink">Ana metin rengi.</param>
/// <param name="Primary">Ana renk (birincil düğme, bağlantı, fiyat vurgusu). Firmanın kendi ana rengi
/// (<c>SiteVurguRengi</c>) bunu ezer.</param>
/// <param name="Accent">Vurgu (işaret, süs çizgisi, rozet zemini). null → ana rengi izler (firma rengi seçilince o da değişir).</param>
public sealed record SchemeColors(string Bg, string Surface, string Ink, string Primary, string? Accent = null)
{
    public string? BgAlt { get; init; }
    public string? SurfaceRaised { get; init; }
    public string? InkMuted { get; init; }
    public string? InkSubtle { get; init; }
    public string? Line { get; init; }
    public string? LineStrong { get; init; }
    /// <summary>Vurgu dolgusu üstündeki yazı (yalnız <see cref="Accent"/> doluyken anlamlı).</summary>
    public string? AccentInk { get; init; }
}

/// <summary>
/// Temanın hazır renk seti. Anahtar KALICI (DB'de <c>TenantSettings.SitePaleti</c>, sitede <c>?palet=</c>):
/// biçim <c>^[a-z0-9-]{1,30}$</c>, tema içinde tekil; listesi <c>Application/TenantSettings/SitePaletteKeys</c>
/// ile aynı olmalı (yapısal test). Temanın İLK seti varsayılandır.
/// </summary>
/// <param name="Key">Kalıcı anahtar.</param>
/// <param name="Ad">ERP'de görünen ad.</param>
/// <param name="Light">Açık şema (<c>prefers-color-scheme: light</c>).</param>
/// <param name="Dark">Koyu şema.</param>
public sealed record SitePalette(string Key, string Ad, SchemeColors Light, SchemeColors Dark);
