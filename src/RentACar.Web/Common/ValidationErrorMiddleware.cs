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
public sealed class ValidationErrorMiddleware(ILogger<ValidationErrorMiddleware> log) : IMiddleware
{
    /// <summary>Mesaj URL'de taşınıyor; makul bir sınır (uzun mesaj zaten okunmaz).</summary>
    private const int MaxMessage = 300;

    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        try
        {
            await next(ctx);
        }
        // F1.2: /api/ui kendi sözleşmesini (ProblemDetails, UiApiExtensions) uygular — burada
        // yönlendirme ya da {ok,hata} gövdesi üretilmez, istisna dıştaki /api/ui çitine bırakılır.
        catch (ValidationException ex) when (!RentACar.Web.Api.UiApiExtensions.UiPath(ctx.Request.Path))
        {
            // Gövde yazılmaya başladıysa müdahale edemeyiz (yarım yanıta yönlendirme eklenemez).
            // Döngü yok: hedef /app (statik SPA kabuğu, ValidationException üretmez).
            if (ctx.Response.HasStarted)
                throw;

            log.LogWarning(ex, "Doğrulama hatası sayfa yolunda yakalandı: {Yol}", ctx.Request.Path);

            var message = ex.Message.Length > MaxMessage ? ex.Message[..MaxMessage] : ex.Message;

            // JSON bekleyen çağıran (kira formundaki fetch'ler gibi) yönlendirmeyi ANLAMAZ —
            // 302'yi izleyip HTML alır ve sessizce bozulur. Ona makine-okunur 400 döneriz.
            if (ExpectsJson(ctx.Request))
            {
                ctx.Response.Clear();
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, hata = message }));
                return;
            }

            // F13.1b: Blazor /hata sayfası yerine yeni arayüzün Panel'i + hata bandı (?hata=).
            ctx.Response.Clear();
            ctx.Response.Redirect(RentACar.Web.Spa.Cutover.ErrorTarget(message));
        }
    }

    private static bool ExpectsJson(HttpRequest req)
        => req.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
           || string.Equals(req.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
