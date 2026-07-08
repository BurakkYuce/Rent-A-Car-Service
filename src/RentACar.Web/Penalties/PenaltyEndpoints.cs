using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Application.Authorization;
using RentACar.Application.Penalties;
using RentACar.Web.Identity;

namespace RentACar.Web.Penalties;

/// <summary>Ceza form post uçları (kayıt + yansıt/öde/iptal). Tenant HttpContext claim'inden (RLS).</summary>
public static class PenaltyEndpoints
{
    public static IEndpointRouteBuilder MapPenaltyEndpoints(this IEndpointRouteBuilder app)
    {
        // Çift savunma (adversarial LOW): create/öde/iptal OperationsWrite; yansıt PARA yolu (FinanceWrite). Tek
        // grup OperationsWrite yansıt'ı Muhasebe'ye kapatırdı → aynı /cezalar prefix'inde iki alt-grup.
        var ops = app.MapGroup("/cezalar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();
        var fin = app.MapGroup("/cezalar").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        ops.MapPost("/create", async (PenaltyService svc,
            [FromForm] string cezaTuru, [FromForm] string? tebligTarihi, [FromForm] string? vadeGun,
            [FromForm] string? vehicleId, [FromForm] string? cariId, [FromForm] string? rentalId,
            [FromForm] decimal tutar, [FromForm] string? sebep) =>
        {
            var input = new PenaltyInput
            {
                CezaTuru = cezaTuru,
                TebligTarihi = FormParse.Date(tebligTarihi),
                VadeGun = FormParse.Int(vadeGun) ?? 15,
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

        fin.MapPost("/yansit", async (PenaltyService svc, [FromForm] Guid id) => await Act(() => svc.YansitAsync(id)));
        ops.MapPost("/ode", async (PenaltyService svc, [FromForm] Guid id) => await Act(() => svc.OdeAsync(id)));
        ops.MapPost("/iptal", async (PenaltyService svc, [FromForm] Guid id) => await Act(() => svc.IptalAsync(id)));

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
