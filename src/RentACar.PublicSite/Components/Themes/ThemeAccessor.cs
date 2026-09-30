using RentACar.Application.Fleet;

namespace RentACar.PublicSite.Components.Themes;

/// <summary>
/// İsteğin teması — istek kapsamlı (scoped), bir kez çözülür. Öncelik: geçerli <c>?tema=</c> önizleme
/// parametresi → firmanın kayıtlı teması (<c>TenantSettings.SiteTemasi</c>) → varsayılan.
///
/// <para>ÖNİZLEME kalıcı değildir (çerez/depolama yok) ve arama motoruna kapalıdır: NoIndexHeaderMiddleware
/// geçerli <c>tema</c> parametresi gördüğünde <c>X-Robots-Tag: noindex</c> yazar; kanonik adres sorgusuz
/// olduğu için değişmez. Geçersiz ad yok sayılır (kayıtlı tema basılır).</para>
/// </summary>
public sealed class ThemeAccessor(FleetShowcaseService showcase, IHttpContextAccessor http)
{
    public const string QueryKey = "tema";

    private SiteTheme? _theme;

    public async Task<SiteTheme> GetAsync()
    {
        if (_theme is not null) return _theme;

        var preview = http.HttpContext?.Request.Query[QueryKey].ToString();
        if (SiteThemes.IsKnown(preview)) return _theme = SiteThemes.Resolve(preview);

        try
        {
            return _theme = SiteThemes.Resolve((await showcase.GetBrandingAsync()).Tema);
        }
        catch
        {
            // Tenant çözülemedi (ör. 404 kabuğu) → varsayılan tema; sayfa yine basılır.
            return _theme = SiteThemes.Tarife;
        }
    }
}
