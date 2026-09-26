using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Authorization;

/// <summary>Ekran yetki override yönetimi (roadmap E3). ManageUsers (Admin). Override = floor üstüne
/// deny-by-default sıkılaştırma; PermissionGuard floor'u değişmez.</summary>
public static class PermissionEndpoints
{
    public static IEndpointRouteBuilder MapPermissionEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/yetki").RequirePermission(Permission.ManageUsers).AntiforgeryByEnv();

        grp.MapPost("/set", async (ScreenPermissionService svc, HttpRequest req) =>
        {
            try
            {
                var code = req.Form["ekranKodu"].ToString();
                var roller = req.Form["roller"]
                    .Select(s => Enum.TryParse<UserRole>(s, out var r) ? (UserRole?)r : null)
                    .Where(r => r is not null).Select(r => r!.Value);
                var active = req.Form["aktif"].ToString() != "false";
                await svc.SetAsync(code, roller, active);
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

        // Yetki şablonu/kopyala (roadmap M2): kaynak rolün ekran erişimini hedef role klonla (sadece ekleme).
        grp.MapPost("/kopyala", async (ScreenPermissionService svc, HttpRequest req) =>
        {
            try
            {
                if (!Enum.TryParse<UserRole>(req.Form["kaynak"].ToString(), out var source) ||
                    !Enum.TryParse<UserRole>(req.Form["hedef"].ToString(), out var target))
                    return Results.Redirect($"/yetki?hata={Uri.EscapeDataString("Kaynak ve hedef rol seçilmelidir.")}");
                var n = await svc.CopyRoleAsync(source, target);
                return Results.Redirect($"/yetki?ok={n}");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/yetki?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        // PR-D: yetki grubu / şablon — mevcut ekran-izni yapılandırmasını isimli profil olarak kaydet/uygula/sil.
        grp.MapPost("/grup/kaydet", async (ScreenPermissionService svc, HttpRequest req) =>
        {
            try { await svc.SnapshotGroupAsync(req.Form["ad"].ToString()); return Results.Redirect("/yetki?ok=1"); }
            catch (ValidationException ex) { return Results.Redirect($"/yetki?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/grup/uygula", async (ScreenPermissionService svc, HttpRequest req) =>
        {
            try { var n = await svc.ApplyGroupAsync(req.Form["ad"].ToString()); return Results.Redirect($"/yetki?ok={n}"); }
            catch (ValidationException ex) { return Results.Redirect($"/yetki?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/grup/sil", async (ScreenPermissionService svc, HttpRequest req) =>
        {
            await svc.DeleteGroupAsync(req.Form["ad"].ToString());
            return Results.Redirect("/yetki?ok=1");
        });

        return app;
    }
}
