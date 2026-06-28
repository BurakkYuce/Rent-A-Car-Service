using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.ReservationSources;
using RentACar.Web.Identity;

namespace RentACar.Web.ReservationSources;

/// <summary>Rezervasyon kaynağı master form post uçları. OperationsWrite.</summary>
public static class ReservationSourceEndpoints
{
    public static IEndpointRouteBuilder MapReservationSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/rezervasyon-kaynaklari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (ReservationSourceService svc, [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(new ReservationSourceInput { Kod = kod, Ad = ad, Aktif = true })));

        grp.MapPost("/update", async (ReservationSourceService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new ReservationSourceInput { Kod = kod, Ad = ad, Aktif = aktif })));

        grp.MapPost("/delete", async (ReservationSourceService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/rezervasyon-kaynaklari"); }
        catch (ValidationException ex) { return Results.Redirect($"/rezervasyon-kaynaklari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
