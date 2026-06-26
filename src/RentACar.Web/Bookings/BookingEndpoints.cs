using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Bookings;
using RentACar.Application.Common;

namespace RentACar.Web.Bookings;

/// <summary>Rezervasyon + kira form post uçları. Tenant HttpContext claim'inden (RLS).</summary>
public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var rez = app.MapGroup("/rezervasyonlar").RequireAuthorization().DisableAntiforgery();

        rez.MapPost("/create", async (ReservationService svc,
            [FromForm] Guid musteriId, [FromForm] Guid vehicleId,
            [FromForm] DateTimeOffset basTar, [FromForm] DateTimeOffset bitTar,
            [FromForm] decimal gunlukUcret, [FromForm] string? cikisOfisi, [FromForm] string? donusOfisi,
            [FromForm] string? aciklama) =>
        {
            try
            {
                await svc.CreateAsync(new BookingInput
                {
                    MusteriId = musteriId, VehicleId = vehicleId, BasTar = basTar, BitTar = bitTar,
                    GunlukUcret = gunlukUcret, CikisOfisi = cikisOfisi, DonusOfisi = donusOfisi, Aciklama = aciklama
                });
                return Results.Redirect("/rezervasyonlar");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/rezervasyonlar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        rez.MapPost("/confirm", async (ReservationService svc, [FromForm] Guid id) =>
        {
            try { await svc.ConfirmAsync(id); return Results.Redirect("/rezervasyonlar"); }
            catch (ValidationException ex) { return Results.Redirect($"/rezervasyonlar?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        rez.MapPost("/cancel", async (ReservationService svc, [FromForm] Guid id) =>
        {
            try { await svc.CancelAsync(id); return Results.Redirect("/rezervasyonlar"); }
            catch (ValidationException ex) { return Results.Redirect($"/rezervasyonlar?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        rez.MapPost("/convert", async (ReservationService svc, [FromForm] Guid id) =>
        {
            try
            {
                await svc.ConvertToRentalAsync(id);
                return Results.Redirect("/kiralar");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/rezervasyonlar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        var kira = app.MapGroup("/kiralar").RequireAuthorization().DisableAntiforgery();

        kira.MapPost("/create", async (RentalService svc,
            [FromForm] Guid musteriId, [FromForm] Guid vehicleId,
            [FromForm] DateTimeOffset basTar, [FromForm] DateTimeOffset bitTar,
            [FromForm] decimal gunlukUcret, [FromForm] string? cikisOfisi, [FromForm] string? donusOfisi,
            [FromForm] string? aciklama) =>
        {
            try
            {
                await svc.CreateDirectAsync(new BookingInput
                {
                    MusteriId = musteriId, VehicleId = vehicleId, BasTar = basTar, BitTar = bitTar,
                    GunlukUcret = gunlukUcret, CikisOfisi = cikisOfisi, DonusOfisi = donusOfisi, Aciklama = aciklama
                });
                return Results.Redirect("/kiralar");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/kiralar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        kira.MapPost("/cancel", async (RentalService svc, [FromForm] Guid id) =>
        {
            try { await svc.CancelAsync(id); return Results.Redirect("/kiralar"); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }
}
