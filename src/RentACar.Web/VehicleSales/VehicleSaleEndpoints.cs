using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Application.VehicleSales;

namespace RentACar.Web.VehicleSales;

/// <summary>Araç satış form post ucu. Tenant HttpContext claim'inden (RLS).</summary>
public static class VehicleSaleEndpoints
{
    public static IEndpointRouteBuilder MapVehicleSaleEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/satislar").RequireAuthorization().DisableAntiforgery();

        grp.MapPost("/create", async (VehicleSaleService svc,
            [FromForm] Guid vehicleId, [FromForm] Guid aliciCariId, [FromForm] decimal satisNet,
            [FromForm] decimal kdvOrani, [FromForm] string? noterNo, [FromForm] string? doviz,
            [FromForm] decimal? kur, [FromForm] string? aciklama) =>
        {
            var input = new VehicleSaleInput
            {
                VehicleId = vehicleId, AliciCariId = aliciCariId, SatisNet = satisNet, KdvOrani = kdvOrani,
                NoterNo = noterNo, Doviz = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz,
                Kur = kur ?? 1m, Aciklama = aciklama
            };
            try
            {
                await svc.CreateAsync(input);
                return Results.Redirect("/satislar");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/satislar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        return app;
    }
}
