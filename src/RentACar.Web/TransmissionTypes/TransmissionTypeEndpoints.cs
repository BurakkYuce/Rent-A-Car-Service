using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.TransmissionTypes;
using RentACar.Web.Identity;

namespace RentACar.Web.TransmissionTypes;

/// <summary>Vites türü master form post uçları. OperationsWrite.</summary>
public static class TransmissionTypeEndpoints
{
    public static IEndpointRouteBuilder MapTransmissionTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/vites-turleri").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (TransmissionTypeService svc, [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(new TransmissionTypeInput { Kod = kod, Ad = ad, Aktif = true })));

        grp.MapPost("/update", async (TransmissionTypeService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new TransmissionTypeInput { Kod = kod, Ad = ad, Aktif = aktif })));

        grp.MapPost("/delete", async (TransmissionTypeService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/vites-turleri"); }
        catch (ValidationException ex) { return Results.Redirect($"/vites-turleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
