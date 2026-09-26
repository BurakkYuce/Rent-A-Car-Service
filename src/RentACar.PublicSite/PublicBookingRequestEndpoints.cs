using Microsoft.AspNetCore.RateLimiting;
using RentACar.Application.Common;
using RentACar.Application.PublicSite;

namespace RentACar.PublicSite;

/// <summary>
/// PR-8: anonim talep formu POST ucu. Repo'daki İLK anonim YAZMA yüzeyi — üç katman koruma:
/// (1) `booking-request` rate-limit policy'si (IP başına), (2) honeypot alanı (servis sessizce yutar),
/// (3) `UseAntiforgery` (PublicSite pipeline'ında zaten var). Tenant izolasyonu RLS.
///
/// Yol `/rezervasyon-talebi/gonder` — sayfanın KENDİ yolu (`@page "/rezervasyon-talebi"`) ile ÇAKIŞMASIN
/// diye alt-path (RentACar.Web'in `/ayarlar` sayfası + `/ayarlar/kaydet` ucu deseniyle aynı). Aynı yola hem
/// Razor sayfası hem minimal-API konursa ASP.NET `AmbiguousMatchException` fırlatır (canlı duman testinde yakalandı).
/// </summary>
public static class PublicBookingRequestEndpoints
{
    public static IEndpointRouteBuilder MapPublicBookingRequestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/rezervasyon-talebi/gonder", async (HttpRequest req, PublicBookingRequestService svc) =>
        {
            var f = req.Form;
            var input = new PublicBookingRequestInput
            {
                AdSoyad = f["adSoyad"].ToString(),
                Telefon = f["telefon"].ToString(),
                Email = f["email"].ToString(),
                // PR-14: vitrin ilan bazlı → yalnız İLAN KİMLİĞİ formdan gelir.
                // FİYAT VE KDV BAYRAĞI FORMDAN OKUNMAZ — servis bunları ilandan SUNUCU TARAFINDA
                // çözer. Sebep: bu snapshot artık sözleşme fiyatına akıyor (DonusturAsync); formdan
                // alınsaydı ziyaretçi `fiyat=1` ya da `kdvDahil=true` yazıp sözleşme bedelini
                // düşürebilirdi. PR-8'de bu alanlar yalnız bilgi amaçlıydı, artık PARA.
                IlanId = Guid.TryParse(f["ilanId"].ToString(), out var listingId) ? listingId : null,
                Sube = f["sube"].ToString(),
                Not = f["not"].ToString(),
                Website = f["website"].ToString(), // HONEYPOT — gerçek kullanıcı boş bırakır
                BasTar = ParseDate(f["bas"].ToString()) ?? default,
                BitTar = ParseDate(f["bit"].ToString()) ?? default,
            };

            try
            {
                await svc.CreateAsync(input, req.HttpContext.RequestAborted);
                return Results.Redirect("/talep-alindi");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect("/rezervasyon-talebi?hata=" + Uri.EscapeDataString(ex.Message));
            }
        }).RequireRateLimiting("booking-request");

        return app;
    }

    private static DateTimeOffset? ParseDate(string? s)
        => DateTimeOffset.TryParse(s, out var d) ? new DateTimeOffset(d.Date, TimeSpan.Zero) : null;
}
