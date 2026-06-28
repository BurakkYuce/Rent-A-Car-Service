using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Authorization;

/// <summary>Ekran yetki override yönetimi (roadmap E3). ManageUsers (Admin). Override = floor üstüne
/// deny-by-default sıkılaştırma; PermissionGuard floor'u değişmez.</summary>
public static class YetkiEndpoints
{
    public static IEndpointRouteBuilder MapYetkiEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/yetki").RequirePermission(Permission.ManageUsers).AntiforgeryByEnv();

        grp.MapPost("/set", async (ScreenPermissionService svc, HttpRequest req) =>
        {
            try
            {
                var kod = req.Form["ekranKodu"].ToString();
                var roller = req.Form["roller"]
                    .Select(s => Enum.TryParse<UserRole>(s, out var r) ? (UserRole?)r : null)
                    .Where(r => r is not null).Select(r => r!.Value);
                var aktif = req.Form["aktif"].ToString() != "false";
                await svc.SetAsync(kod, roller, aktif);
                return Results.Redirect("/yetki?ok=1");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/yetki?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/sil", async (ScreenPermissionService svc, HttpRequest req) =>
        {
            await svc.RemoveAsync(req.Form["ekranKodu"].ToString());
            return Results.Redirect("/yetki?ok=1");
        });

        return app;
    }
}
