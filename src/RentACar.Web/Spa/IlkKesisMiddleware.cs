using RentACar.Web.Api;
using RentACar.Web.Identity;
using RentACar.Web.Platform;

namespace RentACar.Web.Spa;

/// <summary>
/// F4.6 ilk kesiş — kararları <see cref="IlkKesis"/>'ten alıp uygular. Boru hattında kimlik doğrulama,
/// yetkilendirme, <c>TenantActiveMiddleware</c> ve <c>PlatformIsolationMiddleware</c>'DEN SONRA durur:
/// <list type="bullet">
/// <item>Oturumsuz istek haritadaki (<c>[Authorize]</c>) sayfaya gelirse cookie challenge'ı önce alır
/// (<c>/login?ReturnUrl=…</c>) → buradan <c>/app/giris</c>'e; girişten sonra pilotsa SPA'ya iner.</item>
/// <item>Kapalı firmanın oturumu önce düşürülür (<c>/login?hata=kapali</c>); platform operatörü önce konsola gider.</item>
/// </list>
/// Yalnız GET/HEAD'e dokunur. Pilot bayrağı <see cref="UiApiExtensions.PilotMuAsync"/> ile (API pilot kapısıyla
/// AYNI kaynak, önbelleksiz: bayrak kapanınca yönlendirme ANINDA durur). Bayrak okunamazsa yönlendirme YAPILMAZ
/// (Blazor sayfası açılır — güvenli varsayılan; hata loglanır).
/// </summary>
public sealed class IlkKesisMiddleware(RequestDelegate next, ILogger<IlkKesisMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var istek = ctx.Request;
        if (HttpMethods.IsGet(istek.Method) || HttpMethods.IsHead(istek.Method))
        {
            if (IlkKesis.BlazorGirisMi(istek.Path))
            {
                ctx.Response.Redirect(istek.PathBase + await GirisHedefiAsync(ctx));
                return;
            }

            if (FirmaOturumu(ctx) && IlkKesis.SpaHedefi(istek.Method, istek.Path, istek.QueryString) is { } hedef
                && await PilotMuAsync(ctx))
            {
                ctx.Response.Redirect(istek.PathBase + hedef);
                return;
            }
        }
        await next(ctx);
    }

    private async Task<string> GirisHedefiAsync(HttpContext ctx)
    {
        var user = ctx.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            // Login.razor'un eski davranışı: girişli platform operatörü konsola (tenant girişine değil).
            if (user.HasClaim(PlatformClaims.PlatformAdmin, "true")) return "/platform/tenants";
            if (FirmaOturumu(ctx))
            {
                var donus = ctx.Request.Query[YetkiYonlendirme.DonusParametresi].ToString();
                return IlkKesis.GirisSonrasi(await PilotMuAsync(ctx), donus);
            }
        }
        return IlkKesis.GirisYonlendirmesi(ctx.Request.QueryString);
    }

    private static bool FirmaOturumu(HttpContext ctx)
        => ctx.User.Identity?.IsAuthenticated == true && ctx.User.HasClaim(c => c.Type == IdentityClaims.TenantId);

    private async Task<bool> PilotMuAsync(HttpContext ctx)
    {
        try
        {
            return await UiApiExtensions.PilotMuAsync(ctx);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Pilot bayrağı okunamadı — yönlendirme yapılmadı ({Yol}).", ctx.Request.Path.Value);
            return false;
        }
    }
}
