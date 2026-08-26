using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Currencies;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Currencies;

/// <summary>Döviz master form post uçları. OperationsWrite (operasyonel yapılandırma).</summary>
public static class CurrencyEndpoints
{
    public static IEndpointRouteBuilder MapCurrencyEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/dovizler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (CurrencyService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? sembol, [FromForm] string? ulke) =>
            await Run(() => svc.CreateAsync(new CurrencyInput { Kod = kod, Ad = ad, Sembol = sembol, Ulke = ulke, Aktif = true }), "Kayıt eklendi."));

        grp.MapPost("/update", async (CurrencyService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? sembol, [FromForm] string? ulke, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new CurrencyInput { Kod = kod, Ad = ad, Sembol = sembol, Ulke = ulke, Aktif = aktif }), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (CurrencyService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try { await action(); return Sonuc.Tamam("/dovizler", mesaj); }
        catch (ValidationException ex) { return Results.Redirect($"/dovizler?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
