using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.CustomerGroups;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.CustomerGroups;

/// <summary>Müşteri grubu master form post uçları. OperationsWrite.</summary>
public static class CustomerGroupEndpoints
{
    public static IEndpointRouteBuilder MapCustomerGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/musteri-gruplari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (CustomerGroupService svc, [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(new CustomerGroupInput { Kod = kod, Ad = ad, Aktif = true }), "Kayıt eklendi."));

        grp.MapPost("/update", async (CustomerGroupService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new CustomerGroupInput { Kod = kod, Ad = ad, Aktif = aktif }), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (CustomerGroupService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/musteri-gruplari", message); }
        catch (ValidationException ex) { return Results.Redirect($"/musteri-gruplari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
