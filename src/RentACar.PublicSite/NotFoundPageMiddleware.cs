using Microsoft.AspNetCore.Routing;

namespace RentACar.PublicSite;

/// <summary>
/// Gövdesiz 404 yanıtını kabuklu <c>/not-found</c> sayfasıyla doldurur; DURUM KODU 404 KALIR.
///
/// <para><b>Neden:</b> SEO denetimi H-2 — yayından kalkan ilan, yanlış slug ve eşleşmeyen adres 404
/// dönüyordu ama gövde 0 bayttı: eski bir bağlantıdan gelen ziyaretçi menüsüz beyaz ekran görüyordu.</para>
///
/// <para><b>Neden hazır <c>UseStatusCodePagesWithReExecute</c> değil (ÖLÇÜLDÜ):</b> .NET 10 statik SSR,
/// yanıt durumu 404 iken render'ı kabuğun (MainLayout) async hazırlığını BEKLEMEDEN bitiriyor: 404 sayfası
/// marka, menü ve filo olmadan basılıyordu (sayfa 404'ü kendisi set edince gövdenin hiç gelmemesi de aynı
/// davranış). Bu yüzden yeniden render sırasında durum 200 tutulur ve 404, yanıt başlıkları gönderilirken
/// (<see cref="HttpResponse.OnStarting(Func{Task})"/>) yazılır — arama motoru yine 404 görür, soft-404 yok.</para>
///
/// <para><b>Yeni DI kapsamı ŞART:</b> ilk turda bir Blazor sayfası render edildiyse aynı kapsamda ikinci
/// render "HttpNavigationManager already initialized" ile patlar. Yeni kapsamda tenant, tenant
/// middleware'inden Host'tan yeniden çözülür; bilinmeyen host orada yine gövdesiz 404 alır (bu ara katman
/// ikinci kez devreye girmez) — firma sızmaz.</para>
///
/// <para><b>Sıra:</b> <c>UseRouting</c>'den ÖNCE (yeni yol için uç yeniden seçilsin), tenant çözümlemesinden
/// ÖNCE. Yalnız GET (HEAD, HeadRequestMiddleware'de GET'e çevrilir) ve yanıt BAŞLAMAMIŞSA.</para>
/// </summary>
public sealed class NotFoundPageMiddleware(RequestDelegate next, IServiceScopeFactory scopes)
{
    public const string Path = "/not-found";
    /// <summary>Yeniden render edilen isteği işaretler: sayfa durum kodunu kendisi set etmemeli.</summary>
    public const string ItemKey = "racar.not-found-render";

    public async Task InvokeAsync(HttpContext ctx)
    {
        await next(ctx);

        if (ctx.Response.StatusCode != StatusCodes.Status404NotFound || ctx.Response.HasStarted
            || !HttpMethods.IsGet(ctx.Request.Method) || ctx.Items.ContainsKey(ItemKey)
            || ctx.Request.Path.StartsWithSegments(Path) || IsMachinePath(ctx.Request.Path))
            return;

        await RenderAsync(ctx);
    }

    /// <summary>Görsel ve makine uçları: 404'leri HTML sayfasıyla doldurmak boşa iş (img etiketi onu göstermez;
    /// Caddy ask ucu yalnız durum koduna bakar).</summary>
    private static bool IsMachinePath(PathString p)
        => p.StartsWithSegments("/foto") || p.StartsWithSegments("/blog-kapak")
           || p.StartsWithSegments("/marka") || p.StartsWithSegments("/dogrulama");

    private async Task RenderAsync(HttpContext ctx)
    {
        var originalPath = ctx.Request.Path;
        var originalQuery = ctx.Request.QueryString;
        var originalServices = ctx.RequestServices;
        await using var scope = scopes.CreateAsyncScope();

        ctx.Items[ItemKey] = true;
        ctx.RequestServices = scope.ServiceProvider;
        ctx.Request.Path = Path;
        // Sorgu atılır; YALNIZ tema önizlemesi (?tema=) taşınır: önizlenen sitede kırık bir bağlantı
        // 404 sayfasını da aynı temayla göstermeli (ThemeAccessor sorgudan okur, geçersiz ad yok sayılır).
        var preview = originalQuery.HasValue
            && Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(originalQuery.Value)
                .TryGetValue(Components.Themes.ThemeAccessor.QueryKey, out var theme)
            && Components.Themes.SiteThemes.IsKnown(theme.ToString())
                ? QueryString.Create(Components.Themes.ThemeAccessor.QueryKey, theme.ToString())
                : QueryString.Empty;
        ctx.Request.QueryString = preview;
        ctx.SetEndpoint(null);
        ctx.Request.RouteValues = new RouteValueDictionary();
        // İlk turdaki Blazor render'ının yazdığı başlık ikinci turda Headers.Add ile yeniden eklenir →
        // "same key" istisnası (ölçüldü). Güvenlik başlıkları (dış ara katman) KORUNUR; Response.Clear() onları da silerdi.
        ctx.Response.Headers.Remove("blazor-enhanced-nav");
        ctx.Response.Headers.ContentType = default;
        ctx.Response.Headers.ContentLength = null;
        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });
        try
        {
            await next(ctx);
        }
        finally
        {
            ctx.Request.Path = originalPath;
            ctx.Request.QueryString = originalQuery;
            ctx.RequestServices = originalServices;
        }
    }
}
