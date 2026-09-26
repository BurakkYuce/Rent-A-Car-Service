using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Departments;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Departments;

/// <summary>Departman master form post uçları. OperationsWrite.</summary>
public static class DepartmentEndpoints
{
    public static IEndpointRouteBuilder MapDepartmentEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/departmanlar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (DepartmentService svc, [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(new DepartmentInput { Kod = kod, Ad = ad, Aktif = true }), "Kayıt eklendi."));

        grp.MapPost("/update", async (DepartmentService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new DepartmentInput { Kod = kod, Ad = ad, Aktif = aktif }), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (DepartmentService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/departmanlar", message); }
        catch (ValidationException ex) { return Results.Redirect($"/departmanlar?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
