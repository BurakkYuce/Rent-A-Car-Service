using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Accessories;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Accessories;

/// <summary>Aksesuar master form post uçları. OperationsWrite.</summary>
public static class AccessoryEndpoints
{
    public static IEndpointRouteBuilder MapAccessoryEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/aksesuarlar").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (AccessoryService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? aciklama) =>
            await Run(() => svc.CreateAsync(new AccessoryInput { Kod = kod, Ad = ad, Aciklama = aciklama, Aktif = true })));

        grp.MapPost("/update", async (AccessoryService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? aciklama, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new AccessoryInput { Kod = kod, Ad = ad, Aciklama = aciklama, Aktif = aktif })));

        grp.MapPost("/delete", async (AccessoryService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/aksesuarlar"); }
        catch (ValidationException ex) { return Results.Redirect($"/aksesuarlar?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
