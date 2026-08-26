using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Banks;
using RentACar.Application.Common;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Banks;

/// <summary>Banka master form post uçları. OperationsWrite.</summary>
public static class BankEndpoints
{
    public static IEndpointRouteBuilder MapBankEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/bankalar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (BankService svc, [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(new BankInput { Kod = kod, Ad = ad, Aktif = true }), "Kayıt eklendi."));

        grp.MapPost("/update", async (BankService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new BankInput { Kod = kod, Ad = ad, Aktif = aktif }), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (BankService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try { await action(); return Sonuc.Tamam("/bankalar", mesaj); }
        catch (ValidationException ex) { return Results.Redirect($"/bankalar?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
