using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleSegments;
using RentACar.Web.Identity;

namespace RentACar.Web.VehicleSegments;

/// <summary>Araç segment master form post uçları. OperationsWrite.</summary>
public static class VehicleSegmentEndpoints
{
    public static IEndpointRouteBuilder MapVehicleSegmentEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/segmentler").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (VehicleSegmentService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? aciklama) =>
            await Run(() => svc.CreateAsync(new VehicleSegmentInput { Kod = kod, Ad = ad, Aciklama = aciklama, Aktif = true })));

        grp.MapPost("/update", async (VehicleSegmentService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? aciklama, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new VehicleSegmentInput { Kod = kod, Ad = ad, Aciklama = aciklama, Aktif = aktif })));

        grp.MapPost("/delete", async (VehicleSegmentService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/segmentler"); }
        catch (ValidationException ex) { return Results.Redirect($"/segmentler?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
