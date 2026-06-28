using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleColors;
using RentACar.Web.Identity;

namespace RentACar.Web.VehicleColors;

/// <summary>Renk master form post uçları. OperationsWrite.</summary>
public static class VehicleColorEndpoints
{
    public static IEndpointRouteBuilder MapVehicleColorEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/renkler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (VehicleColorService svc, [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(new VehicleColorInput { Kod = kod, Ad = ad, Aktif = true })));

        grp.MapPost("/update", async (VehicleColorService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new VehicleColorInput { Kod = kod, Ad = ad, Aktif = aktif })));

        grp.MapPost("/delete", async (VehicleColorService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/renkler"); }
        catch (ValidationException ex) { return Results.Redirect($"/renkler?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
