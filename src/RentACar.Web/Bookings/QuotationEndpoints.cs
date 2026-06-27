using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Bookings;

/// <summary>Teklif (quotation) form post uçları. Tenant HttpContext claim'inden (RLS).</summary>
public static class QuotationEndpoints
{
    public static IEndpointRouteBuilder MapQuotationEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/teklifler").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (QuotationService svc,
            [FromForm] Guid musteriId, [FromForm] Guid vehicleId,
            [FromForm] DateTimeOffset basTar, [FromForm] DateTimeOffset bitTar,
            [FromForm] decimal gunlukUcret, [FromForm] string? cikisOfisi, [FromForm] string? donusOfisi,
            [FromForm] string? gecerlilik, [FromForm] string? aciklama) =>
        {
            try
            {
                await svc.CreateAsync(new QuotationInput
                {
                    MusteriId = musteriId, VehicleId = vehicleId, BasTar = basTar, BitTar = bitTar,
                    GunlukUcret = gunlukUcret, CikisOfisi = cikisOfisi, DonusOfisi = donusOfisi,
                    GecerlilikTarihi = FormParse.Date(gecerlilik), Aciklama = aciklama
                });
                return Results.Redirect("/teklifler");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/teklifler?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/gonder", async (QuotationService svc, [FromForm] Guid id) =>
        {
            try { await svc.SendAsync(id); return Results.Redirect("/teklifler"); }
            catch (ValidationException ex) { return Results.Redirect($"/teklifler?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/reddet", async (QuotationService svc, [FromForm] Guid id) =>
        {
            try { await svc.RejectAsync(id); return Results.Redirect("/teklifler"); }
            catch (ValidationException ex) { return Results.Redirect($"/teklifler?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/kabul", async (QuotationService svc, [FromForm] Guid id) =>
        {
            try { await svc.AcceptAsync(id); return Results.Redirect("/rezervasyonlar"); }
            catch (ValidationException ex) { return Results.Redirect($"/teklifler?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }
}
