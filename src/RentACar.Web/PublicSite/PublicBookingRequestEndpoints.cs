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
            try { await svc.ReddetAsync(id); return Results.Redirect("/gelen-talepler?ok=1"); }
            catch (ValidationException ex) { return Geri(ex); }
        });

        // ---- PR-17: yaşam döngüsü ----
        grp.MapPost("/durum", async (PublicBookingRequestService svc, [FromForm] Guid id, [FromForm] int durum) =>
        {
            try
            {
                if (!Enum.IsDefined(typeof(Domain.Entities.PublicBookingRequestDurum), durum))
                    throw new ValidationException("Geçersiz durum.");
                await svc.DurumAtaAsync(id, (Domain.Entities.PublicBookingRequestDurum)durum);
                return Results.Redirect("/gelen-talepler?ok=1");
            }
            catch (ValidationException ex) { return Geri(ex); }
        });

        grp.MapPost("/ustlen", async (PublicBookingRequestService svc, [FromForm] Guid id, [FromForm] string ustlen) =>
        {
            try { await svc.UstlenAsync(id, ustlen == "true"); return Results.Redirect("/gelen-talepler?ok=1"); }
            catch (ValidationException ex) { return Geri(ex); }
        });

        grp.MapPost("/not", async (PublicBookingRequestService svc, [FromForm] Guid id, [FromForm] string metin) =>
        {
            try { await svc.NotEkleAsync(id, metin); return Results.Redirect("/gelen-talepler?ok=1"); }
            catch (ValidationException ex) { return Geri(ex); }
        });

        return app;
    }

    private static IResult Geri(ValidationException ex)
        => Results.Redirect("/gelen-talepler?hata=" + Uri.EscapeDataString(ex.Message));
}
