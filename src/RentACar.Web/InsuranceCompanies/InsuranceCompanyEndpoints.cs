using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.InsuranceCompanies;
using RentACar.Web.Identity;

namespace RentACar.Web.InsuranceCompanies;

/// <summary>Sigorta şirketi master form post uçları. OperationsWrite.</summary>
public static class InsuranceCompanyEndpoints
{
    public static IEndpointRouteBuilder MapInsuranceCompanyEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/sigorta-sirketleri").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (InsuranceCompanyService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? telefon) =>
            await Run(() => svc.CreateAsync(new InsuranceCompanyInput { Kod = kod, Ad = ad, Telefon = telefon, Aktif = true })));

        grp.MapPost("/update", async (InsuranceCompanyService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? telefon, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new InsuranceCompanyInput { Kod = kod, Ad = ad, Telefon = telefon, Aktif = aktif })));

        grp.MapPost("/delete", async (InsuranceCompanyService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/sigorta-sirketleri"); }
        catch (ValidationException ex) { return Results.Redirect($"/sigorta-sirketleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
