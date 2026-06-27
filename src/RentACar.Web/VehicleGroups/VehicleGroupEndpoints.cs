using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleGroups;
using RentACar.Web.Identity;

namespace RentACar.Web.VehicleGroups;

/// <summary>Araç grubu master form post uçları. OperationsWrite (operasyonel yapılandırma).</summary>
public static class VehicleGroupEndpoints
{
    public static IEndpointRouteBuilder MapVehicleGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/arac-gruplari").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (VehicleGroupService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? aciklama) =>
            await Run(() => svc.CreateAsync(new VehicleGroupInput
            { Kod = kod, Ad = ad, Aciklama = aciklama, Aktif = true })));

        grp.MapPost("/update", async (VehicleGroupService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? aciklama, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new VehicleGroupInput
            { Kod = kod, Ad = ad, Aciklama = aciklama, Aktif = aktif })));

        grp.MapPost("/delete", async (VehicleGroupService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/arac-gruplari"); }
        catch (ValidationException ex) { return Results.Redirect($"/arac-gruplari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
