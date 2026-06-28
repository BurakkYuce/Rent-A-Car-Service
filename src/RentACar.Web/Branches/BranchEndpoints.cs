using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Domain.Enums;

using RentACar.Web.Identity;

namespace RentACar.Web.Branches;

/// <summary>Şube yönetimi form uçları — yalnız Admin (RequireRole + servis guard çift savunma).</summary>
public static class BranchEndpoints
{
    public static IEndpointRouteBuilder MapBranchEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/subeler")
            .RequireAuthorization(p => p.RequireRole(nameof(UserRole.Admin)))
            .AntiforgeryByEnv();

        grp.MapPost("/create", async (BranchService svc,
            [FromForm] string kod, [FromForm] string ad,
            [FromForm] string? adres, [FromForm] string? telefon) =>
            await Run(() => svc.CreateAsync(new BranchInput
            { Kod = kod, Ad = ad, Adres = adres, Telefon = telefon, Aktif = true })));

        grp.MapPost("/update", async (BranchService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad,
            [FromForm] string? adres, [FromForm] string? telefon, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new BranchInput
            { Kod = kod, Ad = ad, Adres = adres, Telefon = telefon, Aktif = aktif })));

        grp.MapPost("/delete", async (BranchService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try
        {
            await action();
            return Results.Redirect("/subeler");
        }
        catch (ValidationException ex)
        {
            return Results.Redirect($"/subeler?hata={Uri.EscapeDataString(ex.Message)}");
        }
    }
}
