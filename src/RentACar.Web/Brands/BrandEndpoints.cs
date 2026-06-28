using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Brands;
using RentACar.Application.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Brands;

/// <summary>Marka master form post uçları. OperationsWrite (operasyonel yapılandırma).</summary>
public static class BrandEndpoints
{
    public static IEndpointRouteBuilder MapBrandEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/markalar").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (BrandService svc, [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(new BrandInput { Kod = kod, Ad = ad, Aktif = true })));

        grp.MapPost("/update", async (BrandService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new BrandInput { Kod = kod, Ad = ad, Aktif = aktif })));

        grp.MapPost("/delete", async (BrandService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/markalar"); }
        catch (ValidationException ex) { return Results.Redirect($"/markalar?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
