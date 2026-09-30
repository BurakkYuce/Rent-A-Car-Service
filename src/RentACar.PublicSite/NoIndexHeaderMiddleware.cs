namespace RentACar.PublicSite;

/// <summary>
/// M-3: indekslenmemesi gereken yanıtlara <c>X-Robots-Tag</c> başlığı. Razor sayfalarına
/// <c>&lt;meta name="robots"&gt;</c> basmak yerine başlık: kural TEK yerde, yol listesiyle birlikte
/// test edilir ve sayfa şablonları (tasarım) değişse de kaybolmaz.
///
/// <list type="bullet">
/// <item>İşlem sayfaları (<see cref="SeoEndpoints.TransactionPaths"/> ve alt yolları, ör. talep POST'u):
/// <c>noindex</c> — form akışının ara/sonuç ekranları arama sonucunda anlamsız.</item>
/// <item>Sorgu dizgili <c>/musaitlik</c>: <c>noindex, follow</c> — tarih/ilan kombinasyonları sonsuz;
/// ama sayfadaki ilan linkleri takip edilsin. Parametresiz <c>/musaitlik</c> indekslenebilir kalır.</item>
/// <item>429 (istek sınırı) yanıtı: <c>noindex</c> — geçici bir durum sayfası.</item>
/// </list>
///
/// Başlık yanıt BAŞLAMADAN (<c>OnStarting</c>) karar verilerek yazılır: böylece 429 gibi alt katmanda
/// belirlenen durum kodları da görülür ve yeniden çalıştırılan (re-execute) istekte son yol esas alınır.
/// </summary>
public sealed class NoIndexHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Robots-Tag";

    public Task InvokeAsync(HttpContext ctx)
    {
        // Karar İSTEK GİRİŞİNDEKİ yol ile verilir (re-execute yolu değiştirir; özgün adres esas).
        var decision = Decide(ctx.Request.Path, ctx.Request.QueryString);
        ctx.Response.OnStarting(() =>
        {
            var value = ctx.Response.StatusCode == StatusCodes.Status429TooManyRequests ? "noindex" : decision;
            if (value is not null) ctx.Response.Headers[HeaderName] = value;
            return Task.CompletedTask;
        });
        return next(ctx);
    }

    /// <summary>Saf karar: verilen yol + sorgu için başlık değeri, ya da indekslenebilirse null.</summary>
    public static string? Decide(PathString path, QueryString query)
    {
        var p = (path.Value ?? "").TrimEnd('/');
        foreach (var t in SeoEndpoints.TransactionPaths)
        {
            if (p.Equals(t, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(t + "/", StringComparison.OrdinalIgnoreCase))
                return "noindex";
        }

        if (p.Equals("/musaitlik", StringComparison.OrdinalIgnoreCase) && query.HasValue && query.Value != "?")
            return "noindex, follow";

        return null;
    }
}
