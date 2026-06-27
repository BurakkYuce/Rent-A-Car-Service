using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Vehicles;

/// <summary>
/// Araç create/update/delete form post uçları. Tenant, HttpContext.User claim'inden
/// (ITenantContext → RLS) gelir; servis tenant'tan habersizdir.
/// NOT: PR #1 smoke kolaylığı için antiforgery devre dışı — ÜRETİMDE açılmalı (follow-up).
/// </summary>
public static class VehicleEndpoints
{
    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/vehicles").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        group.MapPost("/create", async (
            VehicleService svc,
            [FromForm] string plaka, [FromForm] string? marka, [FromForm] string? grup,
            [FromForm] string? sube, [FromForm] VehicleStatus durum, [FromForm] int km, [FromForm] FuelType yakit) =>
        {
            var input = Build(plaka, marka, grup, sube, durum, km, yakit);
            try
            {
                await svc.CreateAsync(input);
                return Results.Redirect("/vehicles");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/vehicles?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        group.MapPost("/update", async (
            VehicleService svc,
            [FromForm] Guid id, [FromForm] string plaka, [FromForm] string? marka, [FromForm] string? grup,
            [FromForm] string? sube, [FromForm] VehicleStatus durum, [FromForm] int km, [FromForm] FuelType yakit) =>
        {
            var input = Build(plaka, marka, grup, sube, durum, km, yakit);
            try
            {
                var ok = await svc.UpdateAsync(id, input);
                return ok ? Results.Redirect("/vehicles") : Results.NotFound();
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/vehicles/{id}?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        group.MapPost("/delete", async (VehicleService svc, [FromForm] Guid id) =>
        {
            await svc.DeleteAsync(id);
            return Results.Redirect("/vehicles");
        });

        return app;
    }

    private static VehicleInput Build(
        string plaka, string? marka, string? grup, string? sube, VehicleStatus durum, int km, FuelType yakit)
        => new()
        {
            Plaka = plaka,
            Marka = marka,
            Grup = grup,
            Sube = sube,
            Durum = durum,
            Km = km,
            Yakit = yakit
        };
}
