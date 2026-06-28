using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Application.Users;
using RentACar.Domain.Enums;

using RentACar.Web.Identity;

namespace RentACar.Web.Users;

/// <summary>Kullanıcı yönetimi form uçları — yalnız Admin (RequireRole + servis guard çift savunma).</summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/kullanicilar")
            .RequireAuthorization(p => p.RequireRole(nameof(UserRole.Admin)))
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
