using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.PaymentTypes;
using RentACar.Web.Identity;

namespace RentACar.Web.PaymentTypes;

/// <summary>Ödeme tipi master form post uçları. OperationsWrite.</summary>
public static class PaymentTypeEndpoints
{
    public static IEndpointRouteBuilder MapPaymentTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/odeme-tipleri").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (PaymentTypeService svc, [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(new PaymentTypeInput { Kod = kod, Ad = ad, Aktif = true })));

        grp.MapPost("/update", async (PaymentTypeService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new PaymentTypeInput { Kod = kod, Ad = ad, Aktif = aktif })));

        grp.MapPost("/delete", async (PaymentTypeService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/odeme-tipleri"); }
        catch (ValidationException ex) { return Results.Redirect($"/odeme-tipleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
