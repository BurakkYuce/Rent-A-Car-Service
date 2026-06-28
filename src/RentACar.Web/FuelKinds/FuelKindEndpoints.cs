using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.FuelKinds;
using RentACar.Web.Identity;

namespace RentACar.Web.FuelKinds;

/// <summary>Yakıt türü master form post uçları. OperationsWrite.</summary>
public static class FuelKindEndpoints
{
    public static IEndpointRouteBuilder MapFuelKindEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/yakit-turleri").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (FuelKindService svc, [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(new FuelKindInput { Kod = kod, Ad = ad, Aktif = true })));

        grp.MapPost("/update", async (FuelKindService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new FuelKindInput { Kod = kod, Ad = ad, Aktif = aktif })));

        grp.MapPost("/delete", async (FuelKindService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/yakit-turleri"); }
        catch (ValidationException ex) { return Results.Redirect($"/yakit-turleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
