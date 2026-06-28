using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.DamageFiles;
using RentACar.Web.Identity;

namespace RentACar.Web.DamageFiles;

/// <summary>Hasar dosyası (BAF) form post uçları (kayıt + onay akışı). Tenant claim'inden (RLS).</summary>
public static class DamageFileEndpoints
{
    public static IEndpointRouteBuilder MapDamageFileEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/hasar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (DamageFileService svc,
            [FromForm] Guid vehicleId, [FromForm] string? rentalId, [FromForm] string? cariId,
            [FromForm] string? aciklama, [FromForm] string? tahminiTutar) =>
        {
            var input = new DamageFileInput
            {
                VehicleId = vehicleId,
                RentalId = Guid.TryParse(rentalId, out var r) ? r : null,
                CariId = Guid.TryParse(cariId, out var c) ? c : null,
                Aciklama = aciklama, TahminiTutar = FormParse.Dec(tahminiTutar)
            };
            try
            {
                await svc.CreateAsync(input);
                return Results.Redirect("/hasar");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/hasar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/onaya-gonder", (DamageFileService svc, [FromForm] Guid id) => Act(() => svc.OnayaGonderAsync(id)));
        grp.MapPost("/onayla", (DamageFileService svc, [FromForm] Guid id, [FromForm] string? not) => Act(() => svc.OnaylaAsync(id, not)));
        grp.MapPost("/reddet", (DamageFileService svc, [FromForm] Guid id, [FromForm] string? not) => Act(() => svc.ReddetAsync(id, not)));
        grp.MapPost("/kapat", (DamageFileService svc, [FromForm] Guid id) => Act(() => svc.KapatAsync(id)));

        return app;
    }

    private static async Task<IResult> Act(Func<Task<bool>> action)
    {
        try
        {
            await action();
            return Results.Redirect("/hasar");
        }
        catch (ValidationException ex)
        {
            return Results.Redirect($"/hasar?hata={Uri.EscapeDataString(ex.Message)}");
        }
    }
}
