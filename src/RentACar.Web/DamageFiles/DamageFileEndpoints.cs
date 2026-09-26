using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.DamageFiles;
using RentACar.Web.Common;
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
                return Sonuc.Tamam("/hasar", "Kayıt eklendi.");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/hasar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/onaya-gonder", (DamageFileService svc, [FromForm] Guid id) => Act(() => svc.SendForApprovalAsync(id), "İşlem tamamlandı."));
        grp.MapPost("/onayla", (DamageFileService svc, [FromForm] Guid id, [FromForm] string? not) => Act(() => svc.ApproveAsync(id, not), "İşlem tamamlandı."));
        grp.MapPost("/reddet", (DamageFileService svc, [FromForm] Guid id, [FromForm] string? not) => Act(() => svc.RejectAsync(id, not), "Reddedildi."));
        grp.MapPost("/kapat", (DamageFileService svc, [FromForm] Guid id) => Act(() => svc.CloseAsync(id), "Kapatıldı."));

        return app;
    }

    private static async Task<IResult> Act(Func<Task<bool>> action, string mesaj)
    {
        try
        {
            await action();
            return Sonuc.Tamam("/hasar", mesaj);
        }
        catch (ValidationException ex)
        {
            return Results.Redirect($"/hasar?hata={Uri.EscapeDataString(ex.Message)}");
        }
    }
}
