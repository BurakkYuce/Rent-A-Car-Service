using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleTypes;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.VehicleTypes;

/// <summary>Araç tip master form post uçları. OperationsWrite.</summary>
public static class VehicleTypeEndpoints
{
    public static IEndpointRouteBuilder MapVehicleTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/arac-tipleri").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (VehicleTypeService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? marka,
            [FromForm] string? vites, [FromForm] string? yakit, [FromForm] string? grup) =>
            await Run(() => svc.CreateAsync(new VehicleTypeInput
            { Kod = kod, Ad = ad, Marka = marka, Vites = vites, Yakit = yakit, Grup = grup, Aktif = true }), "Kayıt eklendi."));

        grp.MapPost("/update", async (VehicleTypeService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? marka,
            [FromForm] string? vites, [FromForm] string? yakit, [FromForm] string? grup, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new VehicleTypeInput
            { Kod = kod, Ad = ad, Marka = marka, Vites = vites, Yakit = yakit, Grup = grup, Aktif = aktif }), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (VehicleTypeService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/arac-tipleri", message); }
        catch (ValidationException ex) { return Results.Redirect($"/arac-tipleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
