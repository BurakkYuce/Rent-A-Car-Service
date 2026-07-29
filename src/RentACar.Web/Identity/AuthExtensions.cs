using RentACar.Application.Authorization;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Identity;

/// <summary>
/// Endpoint gruplarını yetki matrisine (RolePermissions) göre rol-gate eder. Matris tek
/// doğruluk kaynağı → web ve servis guard'ları aynı kuralı paylaşır.
/// </summary>
public static class AuthExtensions
{
    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder group, Permission permission)
        => group.RequireAuthorization(p => p.RequireRole(RolePermissions.RolesWith(permission)));

    /// <summary>
    /// PR-12: "Web Sitesi" modülü satın alınmamışsa uç grubunu 404'e çevirir (403 değil — modülün
    /// varlığı da bilgi). Menüyü gizlemek YALNIZ görseldir; modülü almayan tenant URL'i elle yazıp
    /// ilan yazabilir ve satın aldığı gün her şey ANİDEN yayına girerdi. CLAUDE.md §6: "guard GİRİŞ
    /// noktasında + doğrulama SUNUCUDA".
    /// </summary>
    public static RouteGroupBuilder RequireWebSitesiModulu(this RouteGroupBuilder group)
        => group.AddEndpointFilter(async (ctx, next) =>
        {
            var tenant = ctx.HttpContext.RequestServices.GetRequiredService<ITenantContext>().TenantId;
            if (tenant is not { } id) return Results.NotFound();
            var cache = ctx.HttpContext.RequestServices.GetRequiredService<TenantStatusCache>();
            if (!await cache.WebSitesiModuluAsync(id, ctx.HttpContext.RequestAborted))
                return Results.NotFound();
            return await next(ctx);
        });

    /// <summary>
    /// Antiforgery'yi ORTAMA göre uygular (roadmap E2, review #7): PROD'da token ZORUNLU (CSRF koruması),
    /// dev/test'te gevşek (geliştirme kolaylığı). Tüm form-POST grupları DisableAntiforgery() yerine bunu
    /// çağırır → tek anahtarla yönetilir. Formlar <AntiforgeryToken/> taşır (prod'da doğrulanır).
    /// </summary>
    public static TBuilder AntiforgeryByEnv<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => FormSecurity.EnforceAntiforgery ? builder : builder.DisableAntiforgery();
}

/// <summary>Antiforgery zorunluluğu anahtarı — Program startup'ta ortamdan (IsProduction) set edilir.</summary>
public static class FormSecurity
{
    public static bool EnforceAntiforgery { get; set; }
}
