namespace RentACar.PublicSite;

/// <summary>
/// PR-2: ince sarmalayıcı — <see cref="PublicTenantResolver"/> ile Host header'ını tenant'a çözer,
/// `Found` DEĞİLSE 404 (asla varsayılan/başka bir tenant'a düşmez — hem bilinmeyen host hem kapalı
/// tenant hem kapalı site aynı sonucu verir, hangisi olduğu dışarıya sızdırılmaz). `Found` ise istek
/// scope'undaki <see cref="PublicTenantContext"/> doldurulur; geri kalan DI akışı (IDbContextFactory
/// üzerinden) TenantConnectionInterceptor ile normal şekilde RLS GUC'unu ayarlar.
/// </summary>
public sealed class TenantHostResolutionMiddleware(PublicTenantResolver resolver) : IMiddleware
{
    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        if (ShouldSkip(ctx.Request.Path))
        {
            await next(ctx);
            return;
        }

        var result = await resolver.ResolveAsync(ctx.Request.Host.Host, ctx.RequestAborted);
        if (result.Kind != PublicTenantResolution.Found)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ctx.RequestServices.GetRequiredService<PublicTenantContext>().TenantId = result.TenantId;
        await next(ctx);
    }

    private static bool ShouldSkip(PathString path)
    {
        var p = path.Value ?? "";
        return p.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            || Path.HasExtension(p);
    }
}
