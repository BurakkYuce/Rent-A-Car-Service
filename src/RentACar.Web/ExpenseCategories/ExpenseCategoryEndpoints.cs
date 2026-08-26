using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.ExpenseCategories;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.ExpenseCategories;

/// <summary>Gider türü master form post uçları. OperationsWrite.</summary>
public static class ExpenseCategoryEndpoints
{
    public static IEndpointRouteBuilder MapExpenseCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/gider-turleri").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (ExpenseCategoryService svc, [FromForm] string kod, [FromForm] string ad, [FromForm] string? tur) =>
            await Run(() => svc.CreateAsync(new ExpenseCategoryInput { Kod = kod, Ad = ad, Tur = tur, Aktif = true }), "Kayıt eklendi."));

        grp.MapPost("/update", async (ExpenseCategoryService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? tur, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new ExpenseCategoryInput { Kod = kod, Ad = ad, Tur = tur, Aktif = aktif }), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (ExpenseCategoryService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try { await action(); return Sonuc.Tamam("/gider-turleri", mesaj); }
        catch (ValidationException ex) { return Results.Redirect($"/gider-turleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
