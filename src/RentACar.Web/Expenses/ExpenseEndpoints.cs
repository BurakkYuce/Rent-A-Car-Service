using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Web.Identity;
using RentACar.Application.Expenses;
using RentACar.Domain.Enums;

namespace RentACar.Web.Expenses;

/// <summary>Gider create form post ucu. Tenant HttpContext claim'inden (RLS).</summary>
public static class ExpenseEndpoints
{
    public static IEndpointRouteBuilder MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/giderler").RequirePermission(Permission.FinanceWrite).DisableAntiforgery();

        grp.MapPost("/create", async (ExpenseService svc,
            [FromForm] ExpenseType tip, [FromForm] string? vehicleId, [FromForm] string? cariId,
            [FromForm] string? sube, [FromForm] string? evrakNo, [FromForm] decimal netTutar,
            [FromForm] decimal kdvOrani, [FromForm] OdemeYontemi odemeYontemi,
            [FromForm] string? doviz, [FromForm] decimal? kur, [FromForm] string? aciklama) =>
        {
            var input = new ExpenseInput
            {
                Tip = tip,
                VehicleId = Guid.TryParse(vehicleId, out var v) ? v : null,
                CariId = Guid.TryParse(cariId, out var c) ? c : null,
                Sube = sube, EvrakNo = evrakNo,
                NetTutar = netTutar, KdvOrani = kdvOrani, OdemeYontemi = odemeYontemi,
                Doviz = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, Kur = kur ?? 1m, Aciklama = aciklama
            };
            try
            {
                await svc.CreateAsync(input);
                return Results.Redirect("/giderler");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/giderler?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        return app;
    }
}
