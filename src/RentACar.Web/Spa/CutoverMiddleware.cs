using RentACar.Web.Identity;
using RentACar.Web.Platform;

namespace RentACar.Web.Spa;

/// <summary>
/// Blazor → yeni arayüz (<c>/app</c>) kesişi — kararları <see cref="Cutover"/>'ten alıp uygular. Boru hattında kimlik
/// doğrulama, yetkilendirme, <c>TenantActiveMiddleware</c> ve <c>PlatformIsolationMiddleware</c>'DEN SONRA durur
/// (kapalı firmanın oturumu önce düşürülür; platform operatörü önce konsola gider). Yalnız GET/HEAD'e dokunur:
/// <list type="bullet">
/// <item><c>/login</c> → oturumsuz <c>/app/giris</c> (dönüş süzülmüş), oturumlu <see cref="Cutover.AfterLogin"/> (302;
/// oturuma bağlı olduğu için kalıcı DEĞİL).</item>
/// <item>Eski Blazor sayfa adresleri (<see cref="Cutover.Map"/>) → SPA karşılığı, <b>301</b>, HERKES için (F13.1b: pilot
/// kapısı ve oturum koşulu kalktı — sayfa yok, tek karşılık SPA; oturum kapısı SPA'da ve <c>/api/ui</c>'de).</item>
/// <item>Eski kabuk sayfaları (<c>/Error</c>, <c>/not-found</c>, <c>/hata</c>, <c>/yetkisiz</c>) → SPA Panel + hata bandı
/// (302; mesaj taşıdığı için).</item>
/// </list>
/// </summary>
public sealed class CutoverMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var request = ctx.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            if (Cutover.IsBlazorEntry(request.Path))
            {
                ctx.Response.Redirect(request.PathBase + LoginTarget(ctx));
                return;
            }

            if (Cutover.SpaTarget(request.Method, request.Path, request.QueryString) is { } target)
            {
                ctx.Response.Redirect(request.PathBase + target, permanent: true);
                return;
            }

            if (Cutover.ShellTarget(request.Path, request.QueryString) is { } shell)
            {
                ctx.Response.Redirect(request.PathBase + shell);
                return;
            }
        }
        await next(ctx);
    }

    private static string LoginTarget(HttpContext ctx)
    {
        var user = ctx.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            // Girişli platform operatörü konsola (tenant girişine değil).
            if (user.HasClaim(PlatformClaims.PlatformAdmin, "true")) return Cutover.SpaPlatformTenants;
            if (user.HasClaim(c => c.Type == IdentityClaims.TenantId))
                return Cutover.AfterLogin(ctx.Request.Query[PermissionRedirect.ReturnParameter].ToString());
        }
        return Cutover.LoginRedirect(ctx.Request.QueryString);
    }
}
