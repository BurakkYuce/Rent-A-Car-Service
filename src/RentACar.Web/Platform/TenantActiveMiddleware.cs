using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using RentACar.Web.Identity;

namespace RentACar.Web.Platform;

/// <summary>
/// Anlık kesme: authenticated + tenant_id claim'i olan istekte tenant KAPALI ise (TenantStatusCache)
/// oturumu düşürüp /login'e yönlendirir. Kapatılan tenant'ın ZATEN AÇIK oturumu bir sonraki istekte
/// kesilir (static SSR'da tam anlık; interaktif circuit'te bir sonraki gezinmede). Skip-path'ler YALNIZ
/// altyapı (login/auth/platform/framework/blazor/content/health/statik varlık) — hiçbir tenant-VERİ yolu
/// atlanmaz (kapalı tenant veri endpoint'inden de iş yapamaz). Platform istekleri (tenant_id yok) etkilenmez.
/// </summary>
public sealed class TenantActiveMiddleware(TenantStatusCache status) : IMiddleware
{
    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        if (!ShouldSkip(ctx.Request.Path) && ctx.User.Identity?.IsAuthenticated == true)
        {
            var tid = ctx.User.FindFirst(IdentityClaims.TenantId)?.Value;
            if (Guid.TryParse(tid, out var tenantId) && !await status.IsActiveAsync(tenantId, ctx.RequestAborted))
            {
                await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                ctx.Response.Redirect("/login?hata=kapali");
                return;
            }
        }
        await next(ctx);
    }

    private static bool ShouldSkip(PathString path)
    {
        var p = path.Value ?? "";
        return p.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/auth", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/platform", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/_content", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || Path.HasExtension(p); // statik varlıklar (.css/.js/.woff2 …) — sayfa değil
    }
}
