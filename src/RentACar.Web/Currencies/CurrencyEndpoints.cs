using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Currencies;
using RentACar.Web.Identity;

namespace RentACar.Web.Currencies;

/// <summary>Döviz master form post uçları. OperationsWrite (operasyonel yapılandırma).</summary>
public static class CurrencyEndpoints
{
    public static IEndpointRouteBuilder MapCurrencyEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/dovizler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (CurrencyService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? sembol) =>
            await Run(() => svc.CreateAsync(new CurrencyInput { Kod = kod, Ad = ad, Sembol = sembol, Aktif = true })));

        grp.MapPost("/update", async (CurrencyService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? sembol, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new CurrencyInput { Kod = kod, Ad = ad, Sembol = sembol, Aktif = aktif })));

        grp.MapPost("/delete", async (CurrencyService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/dovizler"); }
        catch (ValidationException ex) { return Results.Redirect($"/dovizler?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
