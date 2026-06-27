using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Application.Penalties;

namespace RentACar.Web.Penalties;

/// <summary>Ceza form post uçları (kayıt + yansıt/öde/iptal). Tenant HttpContext claim'inden (RLS).</summary>
public static class PenaltyEndpoints
{
    public static IEndpointRouteBuilder MapPenaltyEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/cezalar").RequireAuthorization().DisableAntiforgery();

        grp.MapPost("/create", async (PenaltyService svc,
            [FromForm] string cezaTuru, [FromForm] DateTimeOffset? tebligTarihi, [FromForm] int? vadeGun,
            [FromForm] string? vehicleId, [FromForm] string? cariId, [FromForm] string? rentalId,
            [FromForm] decimal tutar, [FromForm] string? sebep) =>
        {
            var input = new PenaltyInput
            {
                CezaTuru = cezaTuru,
                TebligTarihi = tebligTarihi,
                VadeGun = vadeGun ?? 15,
                VehicleId = Guid.TryParse(vehicleId, out var v) ? v : null,
                CariId = Guid.TryParse(cariId, out var c) ? c : null,
                RentalId = Guid.TryParse(rentalId, out var r) ? r : null,
                Tutar = tutar, Sebep = sebep
            };
            try
            {
                await svc.CreateAsync(input);
                return Results.Redirect("/cezalar");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/cezalar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/yansit", async (PenaltyService svc, [FromForm] Guid id) => await Act(() => svc.YansitAsync(id)));
        grp.MapPost("/ode", async (PenaltyService svc, [FromForm] Guid id) => await Act(() => svc.OdeAsync(id)));
        grp.MapPost("/iptal", async (PenaltyService svc, [FromForm] Guid id) => await Act(() => svc.IptalAsync(id)));

        return app;
    }

    private static async Task<IResult> Act(Func<Task<bool>> action)
    {
        try
        {
            await action();
            return Results.Redirect("/cezalar");
        }
        catch (ValidationException ex)
        {
            return Results.Redirect($"/cezalar?hata={Uri.EscapeDataString(ex.Message)}");
        }
    }
}
