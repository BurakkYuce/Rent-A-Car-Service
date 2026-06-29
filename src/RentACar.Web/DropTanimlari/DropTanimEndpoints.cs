using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.DropTanimlari;
using RentACar.Web.Identity;

namespace RentACar.Web.DropTanimlari;

/// <summary>Drop matris master form post uçları (roadmap N2). OperationsWrite.</summary>
public static class DropTanimEndpoints
{
    public static IEndpointRouteBuilder MapDropTanimEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/drop-tanimlari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (DropTanimService svc, [FromForm] string lokasyon, [FromForm] string sube,
            [FromForm] string? karsilamaSekli, [FromForm] string? calismaSekli, [FromForm] string? ozelIletisim) =>
            await Run(() => svc.CreateAsync(new DropTanimInput
            { Lokasyon = lokasyon, Sube = sube, KarsilamaSekli = karsilamaSekli, CalismaSekli = calismaSekli, OzelIletisim = ozelIletisim, Aktif = true })));

        grp.MapPost("/delete", async (DropTanimService svc, [FromForm] Guid id) => await Run(() => svc.DeleteAsync(id)));
        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/drop-tanimlari"); }
        catch (ValidationException ex) { return Results.Redirect($"/drop-tanimlari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
