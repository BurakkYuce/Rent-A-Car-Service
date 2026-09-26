using RentACar.Web.Api;
using RentACar.Web.Identity;
using RentACar.Web.Platform;

namespace RentACar.Web.Spa;

/// <summary>
/// F4.6 ilk kesiş — kararları <see cref="Cutover"/>'ten alıp uygular. Boru hattında kimlik doğrulama,
/// yetkilendirme, <c>TenantActiveMiddleware</c> ve <c>PlatformIsolationMiddleware</c>'DEN SONRA durur:
/// <list type="bullet">
/// <item>Oturumsuz istek haritadaki (<c>[Authorize]</c>) sayfaya gelirse cookie challenge'ı önce alır
/// (<c>/login?ReturnUrl=…</c>) → buradan <c>/app/giris</c>'e; girişten sonra pilotsa SPA'ya iner.</item>
/// <item>Kapalı firmanın oturumu önce düşürülür (<c>/login?hata=kapali</c>); platform operatörü önce konsola gider.</item>
/// </list>
/// Yalnız GET/HEAD'e dokunur. Pilot bayrağı <see cref="UiApiExtensions.IsPilotAsync"/> ile (API pilot kapısıyla
/// AYNI kaynak, önbelleksiz: bayrak kapanınca yönlendirme ANINDA durur). Bayrak okunamazsa yönlendirme YAPILMAZ
/// (Blazor sayfası açılır — güvenli varsayılan; hata loglanır).
/// </summary>
public sealed class CutoverMiddleware(RequestDelegate next, ILogger<CutoverMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var request = ctx.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            if (Cutover.IsBlazorEntry(request.Path))
            {
                ctx.Response.Redirect(request.PathBase + await LoginTargetAsync(ctx));
                return;
            }

            // F12: platform konsolu sayfaları pilot/oturum koşulu olmadan yönlenir (platform bir kiracı değil). Korumalı
            // sayfaların oturumsuz isteği cookie challenge'ı ÖNCE alır (/platform/login → buradan /app/platform/giris).
            if (Cutover.SpaTarget(request.Method, request.Path, request.QueryString) is { } target
                && (Cutover.IsPlatformConsolePath(request.Path) || (CompanySession(ctx) && await IsPilotAsync(ctx))))
            {
                ctx.Response.Redirect(request.PathBase + target);
                return;
            }
        }
        await next(ctx);
    }

    private async Task<string> LoginTargetAsync(HttpContext ctx)
    {
        var user = ctx.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            // Login.razor'un eski davranışı: girişli platform operatörü konsola (tenant girişine değil).
            if (user.HasClaim(PlatformClaims.PlatformAdmin, "true")) return Cutover.SpaPlatformTenants;
            if (CompanySession(ctx))
            {
                var returnInfo = ctx.Request.Query[PermissionRedirect.ReturnParameter].ToString();
                return Cutover.AfterLogin(await IsPilotAsync(ctx), returnInfo);
            }
        }
        return Cutover.LoginRedirect(ctx.Request.QueryString);
    }

    private static bool CompanySession(HttpContext ctx)
        => ctx.User.Identity?.IsAuthenticated == true && ctx.User.HasClaim(c => c.Type == IdentityClaims.TenantId);

    private async Task<bool> IsPilotAsync(HttpContext ctx)
    {
        try
        {
            return await UiApiExtensions.IsPilotAsync(ctx);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Pilot bayrağı okunamadı — yönlendirme yapılmadı ({Yol}).", ctx.Request.Path.Value);
            return false;
        }
    }
}
