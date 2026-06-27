using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Web.Identity;

namespace RentACar.Web.Pricing;

/// <summary>Tarife (rate card) form post uçları. OperationsWrite (fiyat operasyonel yapılandırma).</summary>
public static class RateCardEndpoints
{
    public static IEndpointRouteBuilder MapRateCardEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/tarifeler").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (RateCardService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string grup,
            [FromForm] string? minGun, [FromForm] string? maxGun, [FromForm] string? gunlukUcret,
            [FromForm] string? doviz, [FromForm] string? gecerliBas, [FromForm] string? gecerliBit) =>
            await Run(() => svc.CreateAsync(Build(kod, ad, grup, minGun, maxGun, gunlukUcret, doviz, gecerliBas, gecerliBit, true))));

        grp.MapPost("/update", async (RateCardService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string grup,
            [FromForm] string? minGun, [FromForm] string? maxGun, [FromForm] string? gunlukUcret,
            [FromForm] string? doviz, [FromForm] string? gecerliBas, [FromForm] string? gecerliBit,
            [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, Build(kod, ad, grup, minGun, maxGun, gunlukUcret, doviz, gecerliBas, gecerliBit, aktif))));

        grp.MapPost("/delete", async (RateCardService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static RateCardInput Build(
        string kod, string ad, string grup, string? minGun, string? maxGun, string? gunlukUcret,
        string? doviz, string? gecerliBas, string? gecerliBit, bool aktif) => new()
    {
        Kod = kod, Ad = ad, Grup = grup,
        MinGun = FormParse.Int(minGun) ?? 1,
        MaxGun = FormParse.Int(maxGun) ?? 9999,
        GunlukUcret = FormParse.Dec(gunlukUcret) ?? 0m,
        Doviz = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz,
        GecerliBas = FormParse.Date(gecerliBas),
        GecerliBit = FormParse.Date(gecerliBit),
        Aktif = aktif
    };

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/tarifeler"); }
        catch (ValidationException ex) { return Results.Redirect($"/tarifeler?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
