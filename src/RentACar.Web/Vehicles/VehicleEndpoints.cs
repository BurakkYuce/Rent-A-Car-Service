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
/// Opsiyonel sayısal/enum alanlar (modelYili, vites, filoDurum) boş "" gelince 400 vermesin
/// diye <c>string?</c> alınıp FormParse/Enum.TryParse ile çevrilir.
/// NOT: PR #1 smoke kolaylığı için antiforgery devre dışı — ÜRETİMDE açılmalı (follow-up).
/// </summary>
public static class VehicleEndpoints
{
    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/vehicles").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        group.MapPost("/create", async (
            VehicleService svc,
            [FromForm] string plaka, [FromForm] string? marka, [FromForm] string? tip, [FromForm] string? grup,
            [FromForm] string? segment, [FromForm] string? sipp, [FromForm] string? renk, [FromForm] string? modelYili,
            [FromForm] string? vites, [FromForm] string? sasiNo, [FromForm] string? motorNo,
            [FromForm] string? sube, [FromForm] VehicleStatus durum, [FromForm] string? filoDurum,
            [FromForm] int km, [FromForm] FuelType yakit) =>
        {
            var input = Build(plaka, marka, tip, grup, segment, sipp, renk, modelYili, vites,
                sasiNo, motorNo, sube, durum, filoDurum, km, yakit);
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
            [FromForm] Guid id, [FromForm] string plaka, [FromForm] string? marka, [FromForm] string? tip,
            [FromForm] string? grup, [FromForm] string? segment, [FromForm] string? sipp, [FromForm] string? renk,
            [FromForm] string? modelYili, [FromForm] string? vites, [FromForm] string? sasiNo, [FromForm] string? motorNo,
            [FromForm] string? sube, [FromForm] VehicleStatus durum, [FromForm] string? filoDurum,
            [FromForm] int km, [FromForm] FuelType yakit) =>
        {
            var input = Build(plaka, marka, tip, grup, segment, sipp, renk, modelYili, vites,
                sasiNo, motorNo, sube, durum, filoDurum, km, yakit);
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
        string plaka, string? marka, string? tip, string? grup, string? segment, string? sipp, string? renk,
        string? modelYili, string? vites, string? sasiNo, string? motorNo, string? sube,
        VehicleStatus durum, string? filoDurum, int km, FuelType yakit)
        => new()
        {
            Plaka = plaka,
            Marka = marka,
            Tip = tip,
            Grup = grup,
            Segment = segment,
            Sipp = sipp,
            Renk = renk,
            ModelYili = FormParse.Int(modelYili),
            Vites = ParseEnum<Vites>(vites),
            SasiNo = sasiNo,
            MotorNo = motorNo,
            Sube = sube,
            Durum = durum,
            FiloDurum = ParseEnum<FiloStatus>(filoDurum),
            Km = km,
            Yakit = yakit
        };

    /// <summary>Boş/whitespace/geçersiz → null; aksi halde enum değeri.</summary>
    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>((s ?? string.Empty).Trim(), out var v) ? v : null;
}
