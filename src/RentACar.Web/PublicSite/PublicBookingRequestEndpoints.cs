using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.PublicSite;
using RentACar.Web.Identity;

namespace RentACar.Web.PublicSite;

/// <summary>PR-8: personel tarafı — gelen site taleplerini dönüştür/reddet (OperationsWrite).</summary>
public static class PublicBookingRequestEndpoints
{
    public static IEndpointRouteBuilder MapGelenTalepEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/gelen-talepler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/donustur", async (PublicBookingRequestService svc, [FromForm] Guid id, [FromForm] Guid vehicleId) =>
        {
            try
            {
                var reservationId = await svc.DonusturAsync(id, vehicleId);
                return Results.Redirect($"/rezervasyonlar?vurgu={reservationId}");
            }
            // AvailabilityConflictException ZATEN ValidationException'dan türüyor (araç çakışması dahil).
            catch (ValidationException ex) { return Geri(ex); }
        });

        grp.MapPost("/reddet", async (PublicBookingRequestService svc, [FromForm] Guid id) =>
        {
            try { await svc.ReddetAsync(id); return Results.Redirect("/gelen-talepler"); }
            catch (ValidationException ex) { return Geri(ex); }
        });

        return app;
    }

    private static IResult Geri(ValidationException ex)
        => Results.Redirect("/gelen-talepler?hata=" + Uri.EscapeDataString(ex.Message));
}
