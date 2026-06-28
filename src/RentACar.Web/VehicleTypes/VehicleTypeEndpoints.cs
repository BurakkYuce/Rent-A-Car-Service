using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleTypes;
using RentACar.Web.Identity;

namespace RentACar.Web.VehicleTypes;

/// <summary>Araç tip master form post uçları. OperationsWrite.</summary>
public static class VehicleTypeEndpoints
{
    public static IEndpointRouteBuilder MapVehicleTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/arac-tipleri").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (VehicleTypeService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? marka) =>
            await Run(() => svc.CreateAsync(new VehicleTypeInput { Kod = kod, Ad = ad, Marka = marka, Aktif = true })));

        grp.MapPost("/update", async (VehicleTypeService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? marka, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new VehicleTypeInput { Kod = kod, Ad = ad, Marka = marka, Aktif = aktif })));

        grp.MapPost("/delete", async (VehicleTypeService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/arac-tipleri"); }
        catch (ValidationException ex) { return Results.Redirect($"/arac-tipleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
