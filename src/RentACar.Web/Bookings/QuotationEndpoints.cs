using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Bookings;

/// <summary>Teklif (quotation) form post uçları. Tenant HttpContext claim'inden (RLS).</summary>
public static class QuotationEndpoints
{
    public static IEndpointRouteBuilder MapQuotationEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/teklifler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (QuotationService svc,
            [FromForm] Guid musteriId, [FromForm] Guid vehicleId,
            [FromForm] DateTimeOffset basTar, [FromForm] DateTimeOffset bitTar,
            [FromForm] string? gunlukUcret, [FromForm] string? cikisOfisi, [FromForm] string? donusOfisi,
            [FromForm] string? gecerlilik, [FromForm] string? aciklama, [FromForm] string? fiyatTuru) =>
        {
            try
            {
                await svc.CreateAsync(new QuotationInput
                {
                    MusteriId = musteriId, VehicleId = vehicleId, BasTar = basTar, BitTar = bitTar,
                    GunlukUcret = FormParse.Dec(gunlukUcret) ?? 0m, CikisOfisi = cikisOfisi, DonusOfisi = donusOfisi,
                    GecerlilikTarihi = FormParse.Date(gecerlilik), Aciklama = aciklama, FiyatTuru = fiyatTuru
                });
                return Sonuc.Tamam("/teklifler", "Kayıt eklendi.");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/teklifler?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/gonder", async (QuotationService svc, [FromForm] Guid id) =>
        {
            try { await svc.SendAsync(id); return Sonuc.Tamam("/teklifler", "Gönderildi."); }
            catch (ValidationException ex) { return Results.Redirect($"/teklifler?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/reddet", async (QuotationService svc, [FromForm] Guid id) =>
        {
            try { await svc.RejectAsync(id); return Sonuc.Tamam("/teklifler", "Reddedildi."); }
            catch (ValidationException ex) { return Results.Redirect($"/teklifler?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/kabul", async (QuotationService svc, [FromForm] Guid id) =>
        {
            try { await svc.AcceptAsync(id); return Sonuc.Tamam("/rezervasyonlar", "Kabul edildi."); }
            catch (ValidationException ex) { return Results.Redirect($"/teklifler?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }
}
