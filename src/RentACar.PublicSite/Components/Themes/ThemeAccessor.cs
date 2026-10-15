using RentACar.Application.Fleet;

namespace RentACar.PublicSite.Components.Themes;

/// <summary>İsteğin görünümü: tema + palet seti + (varsa) özel ana renk ("#rrggbb", Rgb'den yeniden yazılmış).</summary>
public sealed record SiteAppearance(SiteTheme Theme, SitePalette Palette, string? CustomPrimary);

/// <summary>
/// İsteğin görünümü — istek kapsamlı (scoped), bir kez çözülür. Her eksen için öncelik: geçerli önizleme
/// parametresi (<see cref="SitePreview"/>: <c>?tema=</c>, <c>?palet=</c>, <c>?renk=</c>) → firmanın kaydı
/// (<c>TenantSettings.SiteTemasi</c> / <c>SitePaleti</c> / <c>SiteVurguRengi</c>) → temanın varsayılanı.
///
/// <para>Palet daima ÇÖZÜLEN temanın setlerinde aranır: önizlenen ya da kayıtlı palet o temaya ait değilse
/// (tema değişti, başka temanın anahtarı) temanın varsayılan seti basılır. ÖNİZLEME kalıcı değildir ve arama
/// motoruna kapalıdır (NoIndexHeaderMiddleware); kanonik adres sorgusuz olduğu için değişmez.</para>
/// </summary>
public sealed class ThemeAccessor(FleetShowcaseService showcase, IHttpContextAccessor http)
{
    public const string QueryKey = SitePreview.ThemeKey;

    private SiteAppearance? _appearance;

    public async Task<SiteTheme> GetAsync() => (await GetAppearanceAsync()).Theme;

    public async Task<SiteAppearance> GetAppearanceAsync()
    {
        if (_appearance is not null) return _appearance;

        var preview = http.HttpContext is { } ctx ? SitePreview.Parse(ctx.Request.Query) : SitePreview.None;
        FleetBranding? branding = null;
        try
        {
            branding = await showcase.GetBrandingAsync();
        }
        catch
        {
            // Tenant çözülemedi (ör. 404 kabuğu) → önizleme ya da varsayılan görünüm; sayfa yine basılır.
        }

        var theme = SiteThemes.Resolve(preview.Theme ?? branding?.Tema);
        var palette = SitePaletteCss.Find(theme, preview.Palette) ?? SitePaletteCss.Resolve(theme, branding?.Paleti);
        var color = preview.Color ?? (Rgb.TryParse(branding?.VurguRengi, out var c) ? c.Hex : null);
        return _appearance = new SiteAppearance(theme, palette, color);
    }
}
