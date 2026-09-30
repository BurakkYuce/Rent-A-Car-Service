using Microsoft.Extensions.Caching.Memory;
using Microsoft.Net.Http.Headers;
using RentACar.Application.Common;
using RentACar.Application.Fleet;
using RentACar.Domain.Common;

namespace RentACar.PublicSite;

/// <summary>
/// Firma logosu ucu: <c>/marka/logo?v={sürüm}</c>. Kaynak <c>TenantSettings.LogoBytes</c> (ERP Ayarlar → Logo).
///
/// <para><b>Tenant:</b> uç uzantısız olduğu için <see cref="TenantHostResolutionMiddleware"/>'den GEÇER; logo
/// isteğin Host'undan çözülen firmanın RLS bağlamında okunur. Başka firmanın logosu bu yoldan okunamaz.</para>
///
/// <para><b>Önbellek:</b> sayfa adresi sürüm taşır (<see cref="FleetBranding.LogoSurum"/>). Sorgudaki sürüm
/// güncelse yanıt bir yıl <c>immutable</c>; eski/eksik sürümle gelen istek 5 dakika — logo değişince yeni
/// adres zaten yeni sürümle basılır. ETag ile koşullu istek 304 döner.</para>
///
/// <para><b>Boyut:</b> yüklenen logo PDF baskısı için 2000 px'e kadar olabilir; site başlığında ~40 px
/// yüksekliğinde. Sunucu şeffaflığı koruyarak küçültür (<see cref="ImageProcessing.TryCreateWebLogo"/>) ve
/// sonucu firma+sürüm anahtarıyla bellekte tutar — her yeni ziyaretçide yeniden ölçeklenmez.</para>
/// </summary>
public static class BrandEndpoints
{
    public static IEndpointRouteBuilder MapBrandEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/marka/logo", async (HttpRequest req, HttpResponse res, FleetShowcaseService showcase,
            ITenantContext tenant, WebLogoCache cache, CancellationToken ct) =>
        {
            var logo = await showcase.GetLogoAsync(ct);
            if (logo is null) return Results.NotFound();

            var etag = $"\"logo-{logo.Surum}\"";
            var current = string.Equals(req.Query["v"], logo.Surum, StringComparison.Ordinal);
            res.Headers.CacheControl = current ? "public, max-age=31536000, immutable" : "public, max-age=300";
            if (EntityTagHeaderValue.TryParseList(req.Headers.IfNoneMatch, out var tags) && tags.Any(t => t.Tag == etag))
                return Results.StatusCode(StatusCodes.Status304NotModified);

            var (bytes, type) = cache.GetOrCreate(tenant.TenantIdOrThrow(), logo);
            return Results.Bytes(bytes, type, entityTag: new EntityTagHeaderValue(etag));
        });
        return app;
    }
}

/// <summary>Küçültülmüş logo önbelleği — CachedPublicTenantResolver'daki gibi DEDİKE MemoryCache (paylaşımlı
/// IMemoryCache'e SizeLimit koymak diğer tüketicileri etkilerdi). Boyut bayt cinsinden, üst sınır 16 MB.</summary>
public sealed class WebLogoCache : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 16 * 1024 * 1024 });

    public (byte[] Bytes, string ContentType) GetOrCreate(Guid tenantId, PublicLogo logo)
    {
        var key = (tenantId, logo.Surum);
        if (_cache.TryGetValue(key, out (byte[], string) hit)) return hit;

        // Küçültülen logo daima PNG. Küçültülemezse (zaten küçük / çözülemedi) özgün dosya; türü İÇERİKTEN —
        // eski kayıtlarda JPEG logo olabilir (PNG zorunluluğu sonradan geldi).
        var resized = ImageProcessing.TryCreateWebLogo(logo.Bytes);
        (byte[], string) entry = resized is not null
            ? (resized, "image/png")
            : (logo.Bytes, ImageValidation.Detect(logo.Bytes) == ImageKind.Jpeg ? "image/jpeg" : "image/png");
        _cache.Set(key, entry, new MemoryCacheEntryOptions
        {
            Size = entry.Item1.Length,
            SlidingExpiration = TimeSpan.FromHours(6),
        });
        return entry;
    }

    public void Dispose() => _cache.Dispose();
}
