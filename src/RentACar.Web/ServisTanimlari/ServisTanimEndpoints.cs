using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.ServisTanimlari;
using RentACar.Web.Identity;

namespace RentACar.Web.ServisTanimlari;

/// <summary>Servis tanım master form post uçları (roadmap N1). OperationsWrite.</summary>
public static class ServisTanimEndpoints
{
    public static IEndpointRouteBuilder MapServisTanimEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/servis-tanimlari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (ServisTanimService svc,
            [FromForm] string kod, [FromForm] string aracTipi, [FromForm] int bakimKm, [FromForm] string? aciklama) =>
            await Run(() => svc.CreateAsync(new ServisTanimInput { Kod = kod, AracTipi = aracTipi, BakimKm = bakimKm, Aciklama = aciklama, Aktif = true })));

        grp.MapPost("/update", async (ServisTanimService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string aracTipi, [FromForm] int bakimKm, [FromForm] string? aciklama, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new ServisTanimInput { Kod = kod, AracTipi = aracTipi, BakimKm = bakimKm, Aciklama = aciklama, Aktif = aktif })));

        grp.MapPost("/delete", async (ServisTanimService svc, [FromForm] Guid id) => await Run(() => svc.DeleteAsync(id)));
        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/servis-tanimlari"); }
        catch (ValidationException ex) { return Results.Redirect($"/servis-tanimlari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
