using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleOwners;
using RentACar.Web.Identity;

namespace RentACar.Web.VehicleOwners;

/// <summary>Araç sahip master form post uçları. OperationsWrite.</summary>
public static class VehicleOwnerEndpoints
{
    public static IEndpointRouteBuilder MapVehicleOwnerEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/arac-sahipleri").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (VehicleOwnerService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? tur) =>
            await Run(() => svc.CreateAsync(new VehicleOwnerInput { Kod = kod, Ad = ad, Tur = tur, Aktif = true })));

        grp.MapPost("/update", async (VehicleOwnerService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? tur, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new VehicleOwnerInput { Kod = kod, Ad = ad, Tur = tur, Aktif = aktif })));

        grp.MapPost("/delete", async (VehicleOwnerService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/arac-sahipleri"); }
        catch (ValidationException ex) { return Results.Redirect($"/arac-sahipleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
