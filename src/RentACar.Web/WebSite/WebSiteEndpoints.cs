using RentACar.Application.Authorization;
using RentACar.Web.Identity;

namespace RentACar.Web.WebSite;

/// <summary>
/// PR-12: "Web Sitesi" modülünün form-POST uçları. Şimdilik iskelet — ilan sihirbazı PR-13'te bu
/// gruba eklenir.
///
/// İKİ KATMANLI KAPI: <see cref="AuthExtensions.RequireWebSitesiModulu"/> (satın alma, platform
/// kararı) + <see cref="Permission.OperationsWrite"/> (rol). Sıra önemli değil, ikisi de zorunlu.
/// Yeni bir <c>Permission</c> enum değeri EKLENMEDİ: matris (`RolePermissions`) sabit bir sözleşme
/// ve yeni değer `PermissionResolverTests`/`ParaYetkiMatrisiTests` gibi matris testlerini
/// dalgalandırır; daraltma ihtiyacı `ScreenPermissionService` ("web-sitesi" ekran kodu) ile karşılanır.
/// </summary>
public static class WebSiteEndpoints
{
    public static IEndpointRouteBuilder MapWebSiteEndpoints(this IEndpointRouteBuilder app)
    {
        _ = app.MapGroup("/web-sitesi")
            .RequirePermission(Permission.OperationsWrite)
            .RequireWebSitesiModulu()
            .AntiforgeryByEnv();

        // PR-13: /ilan/olustur · /ilan/{id}/fiyat · /ilan/{id}/ozellikler buraya gelecek.
        return app;
    }
}
