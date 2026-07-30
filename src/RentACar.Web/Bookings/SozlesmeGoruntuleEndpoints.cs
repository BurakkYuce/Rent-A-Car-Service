namespace RentACar.Web.Bookings;

/// <summary>
/// PR-C — müşterinin açtığı <b>anonim</b> sözleşme adresi: <c>GET /sozlesme/{token}</c>.
///
/// <para><b>Neden ERP host'unda (PublicSite'ta DEĞİL):</b> halka açık site DÖRT kapının ardında —
/// <c>Tenant.IsActive</c> ∧ <c>WebSitesiModulu</c> ∧ <c>PublicSiteEnabled</c> ∧ bir
/// <c>TenantDomain</c> satırı. Oradan servis edilse <b>web sitesi modülünü satın almamış bir firma
/// sözleşme paylaşamazdı</b> ve sitesini kapatanın müşterisindeki eski linkler ölürdü. Sözleşme
/// paylaşımı işlemsel bir iştir, pazarlama içeriği değil → dört kapı da düşürüldü. Emsal:
/// <c>/feed/calendar/{token}.ics</c>.</para>
///
/// <para><b>Uzantısız yol</b> bilinçli: <c>TenantActiveMiddleware</c> yalnız kimliği doğrulanmış
/// istekte çalıştığından (anonimde devreye girmez) uzantı tuzağı yok; uzantısız adres ayrıca
/// "dosya" değil "sayfa" hissi verir, WhatsApp'ta önizlemesi daha temiz görünür.</para>
///
/// <para><b>Başlıklar:</b> <c>no-store</c> (paylaşılan cihazın diskinde/ara-cache'inde kalmasın) +
/// <c>X-Robots-Tag: noindex, nofollow</c> (kaza ile paylaşılan bir link arama motoruna düşerse TC/adres
/// açıkta olmasın). <c>inline</c> — müşteri tarayıcısında açsın, indirme klasörünü doldurmasın.</para>
///
/// <para><b>Rate limit</b> "login" politikası: 32 baytlık token zaten tahmin edilemez, bu ucuz bir
/// kemer (kaba-kuvvet denemesi loglarda gürültü üretmeden durur).</para>
///
/// <para>İptal edilmiş link / pasif tenant / silinmiş görüntü → <b>404</b>. "Vardı ama iptal edildi"
/// bilgisi de sızmaz.</para>
/// </summary>
public static class SozlesmeGoruntuleEndpoints
{
    public static IEndpointRouteBuilder MapSozlesmeGoruntuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sozlesme/{token}", async (string token, SozlesmeGoruntuleService svc,
            HttpResponse res, CancellationToken ct) =>
        {
            var sonuc = await svc.GoruntuleAsync(token, ct);
            if (sonuc is null) return Results.NotFound();

            res.Headers.CacheControl = "no-store";
            res.Headers["X-Robots-Tag"] = "noindex, nofollow";
            res.Headers.ContentDisposition = $"inline; filename=\"{sonuc.DosyaAdi}\"";
            return Results.Bytes(sonuc.Pdf, "application/pdf");
        })
        .AllowAnonymous()
        .RequireRateLimiting("login");

        return app;
    }
}
