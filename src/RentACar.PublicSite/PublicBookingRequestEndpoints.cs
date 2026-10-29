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
            if (SiteDate.IsMalformedTime(f["basSaat"].ToString()) || SiteDate.IsMalformedTime(f["bitSaat"].ToString()))
                return Results.Redirect("/rezervasyon-talebi?hata=" + BookingRequestException.InvalidTime);
            var (start, end) = (ParseDate(f["bas"].ToString()), ParseDate(f["bit"].ToString()));
            // Saat yalnız İKİ tarih ve İKİ saat de geçerliyse kullanılır (/musaitlik ile aynı kural): İstanbul gün+saati
            // → UTC (Npgsql timestamptz yalnız UTC yazar). Biri bile BOŞSA ESKİ davranış: günün UTC gece yarısı
            // (dolu ama biçimsiz saat yukarıda reddedildi).
            var hasTime = false;
            if (start is not null && end is not null
                && SiteDate.ParseTime(f["basSaat"].ToString()) is { } fromTime
                && SiteDate.ParseTime(f["bitSaat"].ToString()) is { } toTime)
            {
                start = SiteDate.At(DateOnly.FromDateTime(start.Value.Date), fromTime).ToUniversalTime();
                end = SiteDate.At(DateOnly.FromDateTime(end.Value.Date), toTime).ToUniversalTime();
                hasTime = true;
            }
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
                BasTar = start ?? default,
                BitTar = end ?? default,
                HasTime = hasTime,
            };

            try
            {
                await svc.CreateAsync(input, req.HttpContext.RequestAborted);
                return Results.Redirect("/talep-alindi");
            }
            catch (ValidationException ex)
            {
                // Adrese yalnız KOD (serbest metin içerik sahteciliğine açık); sayfa metni koddan basar.
                return Results.Redirect("/rezervasyon-talebi?hata="
                    + (ex is BookingRequestException known ? known.Code : BookingRequestException.Generic));
            }
        }).RequireRateLimiting("booking-request");

        return app;
    }

    private static DateTimeOffset? ParseDate(string? s)
        => DateTimeOffset.TryParse(s, out var d) ? new DateTimeOffset(d.Date, TimeSpan.Zero) : null;
}
