using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Locations;
using RentACar.Web.Identity;

namespace RentACar.Web.Locations;

/// <summary>Ofis/Lokasyon master form post uçları. OperationsWrite (operasyonel yapılandırma).</summary>
public static class LocationEndpoints
{
    public static IEndpointRouteBuilder MapLocationEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/lokasyonlar").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (LocationService svc,
            [FromForm] string kod, [FromForm] string ad,
            [FromForm] string? adres, [FromForm] string? telefon, [FromForm] string? sube) =>
            await Run(() => svc.CreateAsync(new LocationInput
            { Kod = kod, Ad = ad, Adres = adres, Telefon = telefon, Sube = sube, Aktif = true })));

        grp.MapPost("/update", async (LocationService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad,
            [FromForm] string? adres, [FromForm] string? telefon, [FromForm] string? sube, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new LocationInput
            { Kod = kod, Ad = ad, Adres = adres, Telefon = telefon, Sube = sube, Aktif = aktif })));

        grp.MapPost("/delete", async (LocationService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/lokasyonlar"); }
        catch (ValidationException ex) { return Results.Redirect($"/lokasyonlar?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
