using System.Text.Json;
using RentACar.Application.Common;

namespace RentACar.Web.Common;

/// <summary>
/// Sayfa render'ı sırasında atılan <see cref="ValidationException"/>'ı kullanıcıya gösterilebilir
/// bir hataya çevirir — 500'e DEĞİL.
///
/// <para><b>Neden var (canlı hata):</b> Operatör, şube kapsamı dışındaki bir kirayı açmaya
/// çalıştığında <c>RentalService.GetAsync</c> → <c>BranchScope.RequireInScope</c> "Bu kayıt şube
/// kapsamınız dışında." fırlatıyor. POST uçları bu istisnayı tek tek yakalayıp <c>?hata=</c> ile
/// dönüyor, ama SAYFA yolunda (<c>OnInitializedAsync</c>) yakalayan kimse yok → istisna boru
/// hattına kadar çıkıyor ve kullanıcı <b>500 Internal Server Error</b> görüyor.</para>
///
/// <para><c>ValidationException</c>'ın kendi XML dokümanı zaten şunu söylüyor: "Web katmanı bunu
/// kullanıcıya gösterilebilir hata olarak ele alır (500 değil, form hatası)". Bu middleware o
/// sözleşmeyi sayfa yolunda da tutar — yüzlerce sayfayı tek tek try/catch'lemek yerine tek yerde.</para>
///
/// <para><b>Neden 500 kötüydü:</b> (a) kullanıcı yapabileceği bir şey olmadığını sanıyor, oysa yalnız
/// yetkisi/kapsamı yetmiyor; (b) 500 izleme tarafında gerçek arıza gibi görünüyor ve alarm gürültüsü
/// üretiyor; (c) Development dışında <c>/Error</c> sayfası hiçbir bağlam vermiyor.</para>
/// </summary>
public sealed class DogrulamaHatasiMiddleware(ILogger<DogrulamaHatasiMiddleware> log) : IMiddleware
{
    /// <summary>Mesaj URL'de taşınıyor; makul bir sınır (uzun mesaj zaten okunmaz).</summary>
    private const int EnFazlaMesaj = 300;

    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        try
        {
            await next(ctx);
        }
        // F1.2: /api/ui kendi sözleşmesini (ProblemDetails, UiApiExtensions) uygular — burada
        // yönlendirme ya da {ok,hata} gövdesi üretilmez, istisna dıştaki /api/ui çitine bırakılır.
        catch (ValidationException ex) when (!RentACar.Web.Api.UiApiExtensions.UiYolu(ctx.Request.Path))
        {
            // Gövde yazılmaya başladıysa müdahale edemeyiz (yarım HTML'e yönlendirme eklenemez).
            // Döngü koruması: /hata sayfasının kendisi hata verirse tekrar oraya yönlendirmeyelim.
            if (ctx.Response.HasStarted || ctx.Request.Path.StartsWithSegments("/hata"))
                throw;

            log.LogWarning(ex, "Doğrulama hatası sayfa yolunda yakalandı: {Yol}", ctx.Request.Path);

            var mesaj = ex.Message.Length > EnFazlaMesaj ? ex.Message[..EnFazlaMesaj] : ex.Message;

            // JSON bekleyen çağıran (kira formundaki fetch'ler gibi) yönlendirmeyi ANLAMAZ —
            // 302'yi izleyip HTML alır ve sessizce bozulur. Ona makine-okunur 400 döneriz.
            if (JsonBekliyor(ctx.Request))
            {
                ctx.Response.Clear();
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, hata = mesaj }));
                return;
            }

            ctx.Response.Clear();
            ctx.Response.Redirect("/hata?mesaj=" + Uri.EscapeDataString(mesaj));
        }
    }

    private static bool JsonBekliyor(HttpRequest req)
        => req.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
           || string.Equals(req.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
