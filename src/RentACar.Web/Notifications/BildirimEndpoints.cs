using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Notifications;
using RentACar.Web.Identity;

namespace RentACar.Web.Notifications;

/// <summary>Bildirim okundu işaretleme uçları. Herhangi bir kimlikli kullanıcı KENDİ tenant'ının
/// bildirimlerini işaretler (RLS izole). Kalıcı bildirimleri scheduler üretir.</summary>
public static class BildirimEndpoints
{
    public static IEndpointRouteBuilder MapBildirimEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/bildirim").RequireAuthorization().AntiforgeryByEnv();

        grp.MapPost("/oku", async (InAppNotificationService svc, [FromForm] Guid id) =>
        {
            await svc.MarkReadAsync(id);
            return Results.Redirect("/bildirimler?ok=1");
        });

        grp.MapPost("/hepsini-oku", async (InAppNotificationService svc) =>
        {
            await svc.MarkAllReadAsync();
            return Results.Redirect("/bildirimler?ok=1");
        });

        return app;
    }
}
