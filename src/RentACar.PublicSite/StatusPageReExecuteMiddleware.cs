using Microsoft.AspNetCore.Routing;

namespace RentACar.PublicSite;

/// <summary>
/// Hata/durum kodunu KORUYARAK gövdeyi başka bir sayfadan render eder (ASP.NET'in
/// <c>UseStatusCodePagesWithReExecute</c> fikri, ama yalnız AÇIKÇA İSTENEN yanıtlarda).
///
/// <para><b>Neden:</b> M-4 — istek sınırı aşılınca 302 → <c>/cok-istek</c> (200) yerine <b>429 +
/// Retry-After</b> dönülmeli (tarayıcı botlara "yavaşla" sinyali; 200 ise kalıcı içerik gibi okunur).
/// L-5 — çözülemeyen eski ilan adresi 302 → <c>/</c> yerine <b>410</b>. İki durumda da ziyaretçi
/// boş bir sayfa görmemeli: gövde olarak var olan Razor sayfası (kabuk dahil) render edilir, durum
/// kodu değişmez. Razor sayfalarına DOKUNULMAZ.</para>
///
/// <para><b>Neden genel bir <c>UseStatusCodePagesWithReExecute</c> değil:</b> o tüm 4xx/5xx'e uygulanır
/// (404'ün gövde davranışı ayrı bir kararın konusu). Burada yalnız <see cref="Request"/> ile işaretlenmiş
/// yanıtlar yeniden çalıştırılır.</para>
///
/// <para><b>Boru hattı sırası:</b> <c>UseRouting</c>'den ÖNCE olmalı — yeniden çalıştırmada uç, yeni yol
/// için YENİDEN seçilir. Rate limiter ve tenant çözümleme bundan SONRA gelir: ilk istekte limiter
/// reddedince tenant hiç çözülmemiştir; ikinci turda <c>/cok-istek</c>'in limiti yoktur ve tenant
/// çözülür, kabuk (marka, menü) normal render edilir.</para>
/// </summary>
public sealed class StatusPageReExecuteMiddleware(RequestDelegate next)
{
    private const string ItemKey = "racar.reexecute-path";

    /// <summary>Bu yanıtın gövdesinin <paramref name="path"/> sayfasından üretilmesini ister.
    /// Durum kodunu çağıran belirler (ör. 429, 410); yeniden çalıştırma onu değiştirmez.</summary>
    public static void Request(HttpContext ctx, string path) => ctx.Items[ItemKey] = path;

    public async Task InvokeAsync(HttpContext ctx)
    {
        await next(ctx);

        if (ctx.Items[ItemKey] is not string path || ctx.Response.HasStarted) return;
        ctx.Items.Remove(ItemKey); // tek tur: hedef sayfa yeniden işaretlerse döngü olmaz

        var status = ctx.Response.StatusCode;
        var originalPath = ctx.Request.Path;
        var originalQuery = ctx.Request.QueryString;
        var originalMethod = ctx.Request.Method;

        // Hedef sayfa GET olarak render edilir (reddedilen istek POST olabilir: talep formu).
        ctx.Request.Path = path;
        ctx.Request.QueryString = QueryString.Empty;
        ctx.Request.Method = HttpMethods.Get;
        ctx.SetEndpoint(null);
        ctx.Request.RouteValues = new RouteValueDictionary();
        ctx.Response.Headers.ContentType = default;
        ctx.Response.Headers.ContentLength = null;
        try
        {
            await next(ctx);
        }
        finally
        {
            ctx.Request.Path = originalPath;
            ctx.Request.QueryString = originalQuery;
            ctx.Request.Method = originalMethod;
        }

        // Sayfa durum kodunu açıkça değiştirmediyse zaten korunur; yanıt başlamadıysa garantiye al.
        if (!ctx.Response.HasStarted) ctx.Response.StatusCode = status;
    }
}
