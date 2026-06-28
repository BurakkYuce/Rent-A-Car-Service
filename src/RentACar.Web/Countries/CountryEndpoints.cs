using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Countries;
using RentACar.Web.Identity;

namespace RentACar.Web.Countries;

/// <summary>Ülke master form post uçları. OperationsWrite.</summary>
public static class CountryEndpoints
{
    public static IEndpointRouteBuilder MapCountryEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/ulkeler").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (CountryService svc, [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(new CountryInput { Kod = kod, Ad = ad, Aktif = true })));

        grp.MapPost("/update", async (CountryService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new CountryInput { Kod = kod, Ad = ad, Aktif = aktif })));

        grp.MapPost("/delete", async (CountryService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/ulkeler"); }
        catch (ValidationException ex) { return Results.Redirect($"/ulkeler?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
