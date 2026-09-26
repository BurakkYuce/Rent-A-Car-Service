using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.PublicSite;
using RentACar.Web.Identity;

namespace RentACar.Web.PublicSite;

/// <summary>PR-8: personel tarafı — gelen site taleplerini dönüştür/reddet (OperationsWrite).</summary>
public static class PublicBookingRequestEndpoints
{
    public static IEndpointRouteBuilder MapIncomingRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/gelen-talepler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/donustur", async (PublicBookingRequestService svc, [FromForm] Guid id, [FromForm] Guid vehicleId) =>
        {
            try
            {
                var reservationId = await svc.ConvertAsync(id, vehicleId);
                return Results.Redirect($"/rezervasyonlar?vurgu={reservationId}");
            }
            // AvailabilityConflictException ZATEN ValidationException'dan türüyor (araç çakışması dahil).
            catch (ValidationException ex) { return Back(ex); }
        });

        grp.MapPost("/reddet", async (PublicBookingRequestService svc, [FromForm] Guid id) =>
        {
            try { await svc.RejectAsync(id); return Results.Redirect("/gelen-talepler?ok=1"); }
            catch (ValidationException ex) { return Back(ex); }
        });

        // ---- PR-17: yaşam döngüsü ----
        grp.MapPost("/durum", async (PublicBookingRequestService svc, [FromForm] Guid id, [FromForm] int durum) =>
        {
            try
            {
                if (!Enum.IsDefined(typeof(Domain.Entities.PublicBookingRequestDurum), durum))
                    throw new ValidationException("Geçersiz durum.");
                await svc.AssignStatusAsync(id, (Domain.Entities.PublicBookingRequestDurum)durum);
                return Results.Redirect("/gelen-talepler?ok=1");
            }
            catch (ValidationException ex) { return Back(ex); }
        });

        grp.MapPost("/ustlen", async (PublicBookingRequestService svc, [FromForm] Guid id, [FromForm] string ustlen) =>
        {
            try { await svc.ClaimAsync(id, ustlen == "true"); return Results.Redirect("/gelen-talepler?ok=1"); }
            catch (ValidationException ex) { return Back(ex); }
        });

        grp.MapPost("/not", async (PublicBookingRequestService svc, [FromForm] Guid id, [FromForm] string metin) =>
        {
            try { await svc.AddNoteAsync(id, metin); return Results.Redirect("/gelen-talepler?ok=1"); }
            catch (ValidationException ex) { return Back(ex); }
        });

        return app;
    }

    private static IResult Back(ValidationException ex)
        => Results.Redirect("/gelen-talepler?hata=" + Uri.EscapeDataString(ex.Message));
}
