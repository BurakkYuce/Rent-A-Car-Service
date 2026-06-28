using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Bookings;

/// <summary>Rezervasyon + kira form post uçları. Tenant HttpContext claim'inden (RLS).</summary>
public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var rez = app.MapGroup("/rezervasyonlar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        rez.MapPost("/create", async (ReservationService svc, HttpRequest req,
            [FromForm] Guid musteriId, [FromForm] Guid vehicleId,
            [FromForm] DateTimeOffset basTar, [FromForm] DateTimeOffset bitTar,
            [FromForm] decimal gunlukUcret, [FromForm] string? cikisOfisi, [FromForm] string? donusOfisi,
            [FromForm] string? aciklama) =>
        {
            try
            {
                var input = new BookingInput
                {
                    MusteriId = musteriId, VehicleId = vehicleId, BasTar = basTar, BitTar = bitTar,
                    GunlukUcret = gunlukUcret, CikisOfisi = cikisOfisi, DonusOfisi = donusOfisi, Aciklama = aciklama
                };
                ApplyOdemeDerinlik(input, req.Form);
                await svc.CreateAsync(input);
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

        var kira = app.MapGroup("/kiralar").RequireAuthorization().AntiforgeryByEnv();

        kira.MapPost("/create", async (RentalService svc, HttpRequest req,
            [FromForm] Guid musteriId, [FromForm] Guid vehicleId,
            [FromForm] DateTimeOffset basTar, [FromForm] DateTimeOffset bitTar,
            [FromForm] decimal gunlukUcret, [FromForm] string? cikisOfisi, [FromForm] string? donusOfisi,
            [FromForm] string? aciklama) =>
        {
            try
            {
                var input = new BookingInput
                {
                    MusteriId = musteriId, VehicleId = vehicleId, BasTar = basTar, BitTar = bitTar,
                    GunlukUcret = gunlukUcret, CikisOfisi = cikisOfisi, DonusOfisi = donusOfisi, Aciklama = aciklama
                };
                ApplyOdemeDerinlik(input, req.Form);
                await svc.CreateDirectAsync(input);
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

        kira.MapPost("/teslim", async (RentalService svc,
            [FromForm] Guid id, [FromForm] int cikisKm, [FromForm] int cikisYakit) =>
        {
            try { await svc.DeliverAsync(id, cikisKm, cikisYakit); return Results.Redirect($"/kiralar/{id}"); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        kira.MapPost("/donus", async (RentalService svc,
            [FromForm] Guid id, [FromForm] int donusKm, [FromForm] int donusYakit, [FromForm] DateTimeOffset gercekDonus) =>
        {
            try { await svc.ReturnAsync(id, donusKm, donusYakit, gercekDonus); return Results.Redirect($"/kiralar/{id}"); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }

    /// <summary>Opsiyonel ödeme-derinlik alanlarını forma göre doldurur (roadmap A2; bilgi amaçlı,
    /// deftere yansımaz). Boş → null (FormParse.Dec).</summary>
    private static void ApplyOdemeDerinlik(BookingInput input, IFormCollection f)
    {
        input.Provizyon = FormParse.Dec(f["provizyon"].ToString());
        input.Depozito = FormParse.Dec(f["depozito"].ToString());
        input.KomisyonOran = FormParse.Dec(f["komisyonOran"].ToString());
        input.KomisyonTutar = FormParse.Dec(f["komisyonTutar"].ToString());
        input.DropUcreti = FormParse.Dec(f["dropUcreti"].ToString());
        input.SonraOdeOran = FormParse.Dec(f["sonraOdeOran"].ToString());
    }
}
