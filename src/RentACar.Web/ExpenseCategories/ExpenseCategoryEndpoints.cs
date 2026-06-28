using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.ExpenseCategories;
using RentACar.Web.Identity;

namespace RentACar.Web.ExpenseCategories;

/// <summary>Gider türü master form post uçları. OperationsWrite.</summary>
public static class ExpenseCategoryEndpoints
{
    public static IEndpointRouteBuilder MapExpenseCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/gider-turleri").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (ExpenseCategoryService svc, [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(new ExpenseCategoryInput { Kod = kod, Ad = ad, Aktif = true })));

        grp.MapPost("/update", async (ExpenseCategoryService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new ExpenseCategoryInput { Kod = kod, Ad = ad, Aktif = aktif })));

        grp.MapPost("/delete", async (ExpenseCategoryService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/gider-turleri"); }
        catch (ValidationException ex) { return Results.Redirect($"/gider-turleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
