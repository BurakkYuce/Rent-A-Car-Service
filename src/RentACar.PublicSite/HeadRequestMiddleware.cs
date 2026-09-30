using Microsoft.AspNetCore.Http.Features;

namespace RentACar.PublicSite;

/// <summary>
/// M-5: HEAD istekleri GET gibi yönlendirilir, gövde atılır. Eskiden <c>curl -I /</c> 404 dönüyordu:
/// Razor bileşen uçları ve <c>MapGet</c> uçları yalnız GET/POST'a eşleşir, HEAD hiçbir uca düşmez.
/// Link denetleyicileri ve izleme araçları HEAD kullandığı için site onlara "kırık" görünüyordu.
///
/// <para><b>Nasıl:</b> yöntem boru hattı boyunca GET yapılır (yönlendirme GET ucunu bulsun) ve yanıt
/// gövdesi <see cref="Stream.Null"/>'a bağlanır — sayfa normal render edilir, baytlar hiçbir yere
/// yazılmaz. Gövde özelliği önceki özelliğe BAĞLANMAZ: bağlansaydı yanıt yöntem hâlâ GET iken
/// başlar ve sunucu HEAD yanıtına gövde çerçevesi yazabilirdi. Yöntem dönüşte HEAD'e geri alınır;
/// başlıklar ondan sonra, HEAD olarak gönderilir.</para>
///
/// <para>Boru hattında yönlendirmeden (<c>UseRouting</c>) ÖNCE olmalı.</para>
/// </summary>
public sealed class HeadRequestMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!HttpMethods.IsHead(ctx.Request.Method))
        {
            await next(ctx);
            return;
        }

        var originalBody = ctx.Features.Get<IHttpResponseBodyFeature>();
        ctx.Request.Method = HttpMethods.Get;
        ctx.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(Stream.Null));
        try
        {
            await next(ctx);
        }
        finally
        {
            ctx.Features.Set(originalBody);
            ctx.Request.Method = HttpMethods.Head;
        }
    }
}
