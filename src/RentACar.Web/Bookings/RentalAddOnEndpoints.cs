using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.RentalAddOns;
using RentACar.Web.Identity;

namespace RentACar.Web.Bookings;

/// <summary>Kira ek hizmet kalemi ekle/sil form post uçları. OperationsWrite.
/// Miktar boş "" ile 400 vermesin diye <c>string?</c> + FormParse.Dec (servis miktar&gt;0 doğrular).</summary>
public static class RentalAddOnEndpoints
{
    public static IEndpointRouteBuilder MapRentalAddOnEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/kiralar").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/ekhizmet-ekle", async (RentalAddOnService svc,
            [FromForm] Guid rentalId, [FromForm] Guid ekHizmetTanimId, [FromForm] string? miktar) =>
        {
            try
            {
                await svc.AddAsync(rentalId, ekHizmetTanimId, FormParse.Dec(miktar) ?? 0m);
                return Results.Redirect($"/kiralar/{rentalId}");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/kiralar/{rentalId}?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/ekhizmet-sil", async (RentalAddOnService svc,
            [FromForm] Guid addOnId, [FromForm] Guid rentalId) =>
        {
            try
            {
                await svc.RemoveAsync(addOnId);
                return Results.Redirect($"/kiralar/{rentalId}");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/kiralar/{rentalId}?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        return app;
    }
}
