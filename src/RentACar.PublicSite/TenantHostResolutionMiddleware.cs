namespace RentACar.PublicSite;

/// <summary>
/// PR-2: ince sarmalayıcı — <see cref="IPublicTenantResolver"/> (PR-3.5'ten beri `CachedPublicTenantResolver`)
/// ile Host header'ını tenant'a çözer, `Found` DEĞİLSE 404 (asla varsayılan/başka bir tenant'a düşmez —
/// hem bilinmeyen host hem kapalı tenant hem kapalı site aynı sonucu verir, hangisi olduğu dışarıya
/// sızdırılmaz). `Found` ise istek scope'undaki <see cref="PublicTenantContext"/> doldurulur; geri kalan
/// DI akışı (IDbContextFactory üzerinden) TenantConnectionInterceptor ile normal şekilde RLS GUC'unu ayarlar.
/// </summary>
public sealed class TenantHostResolutionMiddleware(IPublicTenantResolver resolver) : IMiddleware
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
        // PR-9: uzantılı ama DİNAMİK (tenant-bağımlı) uçlar — `Path.HasExtension` bunları statik dosya
        // sanıp atlardı, tenant çözümlenmeden `TenantIdOrThrow` patlardı (canlı duman testinde yakalandı).
        if (p.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase)
            || p.Equals("/sitemap.xml", StringComparison.OrdinalIgnoreCase))
            return false;

        return p.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            // PR-5: Caddy on_demand_tls ask-endpoint'i — İSTEĞİN KENDİ Host header'ı (Caddy'nin ask isteği,
            // sorulan domain DEĞİL, query string'de) tenant çözümlemesine hiç GİRMEMELİ, platform-seviyesi bir uç.
            || p.StartsWith("/dogrulama", StringComparison.OrdinalIgnoreCase)
            || Path.HasExtension(p);
    }
}
