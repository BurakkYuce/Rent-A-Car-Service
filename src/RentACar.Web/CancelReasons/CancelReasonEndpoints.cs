using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.CancelReasons;
using RentACar.Application.Common;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.CancelReasons;

/// <summary>İptal sebebi master form post uçları. OperationsWrite (operasyonel yapılandırma).</summary>
public static class CancelReasonEndpoints
{
    public static IEndpointRouteBuilder MapCancelReasonEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/iptal-sebepleri").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (CancelReasonService svc, [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(new CancelReasonInput { Kod = kod, Ad = ad, Aktif = true }), "Kayıt eklendi."));

        grp.MapPost("/update", async (CancelReasonService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new CancelReasonInput { Kod = kod, Ad = ad, Aktif = aktif }), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (CancelReasonService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/iptal-sebepleri", message); }
        catch (ValidationException ex) { return Results.Redirect($"/iptal-sebepleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
