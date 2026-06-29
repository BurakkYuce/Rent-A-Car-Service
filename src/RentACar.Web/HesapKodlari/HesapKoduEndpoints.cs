using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.HesapKodlari;
using RentACar.Web.Identity;

namespace RentACar.Web.HesapKodlari;

/// <summary>Hesap kodu master form post uçları (roadmap N1). OperationsWrite.</summary>
public static class HesapKoduEndpoints
{
    public static IEndpointRouteBuilder MapHesapKoduEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/hesap-kodlari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (HesapKoduService svc, [FromForm] string kod, [FromForm] string ad, [FromForm] string? aciklama) =>
            await Run(() => svc.CreateAsync(new HesapKoduInput { Kod = kod, Ad = ad, Aciklama = aciklama, Aktif = true })));

        grp.MapPost("/update", async (HesapKoduService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? aciklama, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new HesapKoduInput { Kod = kod, Ad = ad, Aciklama = aciklama, Aktif = aktif })));

        grp.MapPost("/delete", async (HesapKoduService svc, [FromForm] Guid id) => await Run(() => svc.DeleteAsync(id)));
        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/hesap-kodlari"); }
        catch (ValidationException ex) { return Results.Redirect($"/hesap-kodlari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
