using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.CustomCodes;
using RentACar.Web.Identity;

namespace RentACar.Web.CustomCodes;

/// <summary>Özel kod master form post uçları. OperationsWrite.</summary>
public static class CustomCodeEndpoints
{
    public static IEndpointRouteBuilder MapCustomCodeEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/ozel-kodlar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (CustomCodeService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? aciklama, [FromForm] string? turu) =>
            await Run(() => svc.CreateAsync(new CustomCodeInput { Kod = kod, Ad = ad, Aciklama = aciklama, Turu = turu, Aktif = true })));

        grp.MapPost("/update", async (CustomCodeService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? aciklama, [FromForm] string? turu, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new CustomCodeInput { Kod = kod, Ad = ad, Aciklama = aciklama, Turu = turu, Aktif = aktif })));

        grp.MapPost("/delete", async (CustomCodeService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/ozel-kodlar"); }
        catch (ValidationException ex) { return Results.Redirect($"/ozel-kodlar?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
