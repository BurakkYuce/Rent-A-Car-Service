using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Users;
using RentACar.Domain.Enums;

using RentACar.Web.Identity;

namespace RentACar.Web.Users;

/// <summary>Kullanıcı yönetimi form uçları — ManageUsers etkin izni (policy + servis guard çift savunma).
/// 2026-08-17: RequireRole(Admin) yerine etkin izin — kullanıcı-bazlı ek ManageUsers artık kapıyı açar,
/// yasak kapatır (salt rol kapısı ikisini de görmezdi).</summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/kullanicilar")
            .RequirePermission(Permission.ManageUsers)
            .AntiforgeryByEnv();

        grp.MapPost("/create", async (UserService svc,
            [FromForm] string userName, [FromForm] string? displayName,
            [FromForm] UserRole rol, [FromForm] string password, [FromForm] string? atanmisSube) =>
            await Run(() => svc.CreateAsync(new UserInput
            { UserName = userName, DisplayName = displayName ?? "", Rol = rol, Password = password, AtanmisSube = atanmisSube })));

        grp.MapPost("/aktif", async (UserService svc, [FromForm] Guid id, [FromForm] bool active) =>
            await Run(() => svc.SetActiveAsync(id, active)));

        grp.MapPost("/sifre", async (UserService svc, [FromForm] Guid id, [FromForm] string password) =>
            await Run(() => svc.ResetPasswordAsync(id, password)));

        // ---- Kullanıcı-bazlı izin istisnaları (2026-08-17) ----
        grp.MapPost("/istisna/set", async (KullaniciIzinService svc,
            [FromForm] Guid userId, [FromForm] string izin, [FromForm] string tur) =>
            await Run(() => svc.SetAsync(userId, izin, ver: tur == "ver")));

        grp.MapPost("/istisna/sil", async (KullaniciIzinService svc,
            [FromForm] Guid userId, [FromForm] string izin) =>
            await Run(() => svc.RemoveAsync(userId, izin)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try
        {
            await action();
            return Results.Redirect("/kullanicilar");
        }
        catch (ValidationException ex)
        {
            return Results.Redirect($"/kullanicilar?hata={Uri.EscapeDataString(ex.Message)}");
        }
    }
}
