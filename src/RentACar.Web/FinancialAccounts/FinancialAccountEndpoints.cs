using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.FinancialAccounts;
using RentACar.Web.Identity;

namespace RentACar.Web.FinancialAccounts;

/// <summary>Kasa/Banka hesap master form post uçları. OperationsWrite.</summary>
public static class FinancialAccountEndpoints
{
    public static IEndpointRouteBuilder MapFinancialAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/hesaplar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (FinancialAccountService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? tur, [FromForm] string? doviz) =>
            await Run(() => svc.CreateAsync(new FinancialAccountInput { Kod = kod, Ad = ad, Tur = tur, Doviz = doviz, Aktif = true })));

        grp.MapPost("/update", async (FinancialAccountService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? tur, [FromForm] string? doviz, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new FinancialAccountInput { Kod = kod, Ad = ad, Tur = tur, Doviz = doviz, Aktif = aktif })));

        grp.MapPost("/delete", async (FinancialAccountService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/hesaplar"); }
        catch (ValidationException ex) { return Results.Redirect($"/hesaplar?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
