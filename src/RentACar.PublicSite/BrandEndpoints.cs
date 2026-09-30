using System.Collections.Concurrent;
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
            // ÖNCE yalnız sürüm damgası (bayt YOK): 304 ve önbellek isabeti LogoBytes'ı (1 MB'a kadar) DB'den
            // hiç çekmeden döner. Baytlar yalnız önbellek kaçırmada, tek uçuşta okunur.
            var branding = await showcase.GetBrandingAsync(ct);
            if (branding.LogoSurum is not { } surum) return Results.NotFound();

            var etag = $"\"logo-{surum}\"";
            var current = string.Equals(req.Query["v"], surum, StringComparison.Ordinal);
            res.Headers.CacheControl = current ? "public, max-age=31536000, immutable" : "public, max-age=300";
            if (EntityTagHeaderValue.TryParseList(req.Headers.IfNoneMatch, out var tags) && tags.Any(t => t.Tag == etag))
                return Results.StatusCode(StatusCodes.Status304NotModified);

            var logo = await cache.GetOrCreateAsync(tenant.TenantIdOrThrow(), surum, showcase.GetLogoAsync);
            if (logo is null)
            {
                // Logo arada silinmiş ya da çözme bütçesini aşıyor (kural sıkılaşmadan önce kaydedilmiş dev logo).
                res.Headers.CacheControl = "public, max-age=300";
                return Results.NotFound();
            }
            if (logo.Surum != surum)
            {
                // Damga okunduktan sonra logo değişti: yeni içerik eski sürüm adresine uzun süre yapışmasın.
                res.Headers.CacheControl = "public, max-age=300";
                etag = $"\"logo-{logo.Surum}\"";
            }
            return Results.Bytes(logo.Bytes, logo.ContentType, entityTag: new EntityTagHeaderValue(etag));
        });
        return app;
    }
}

/// <summary>Servis edilecek web logosu (küçültülmüş PNG ya da küçükse özgün dosya) ve hangi sürümden üretildiği.</summary>
public sealed record WebLogo(byte[] Bytes, string ContentType, string Surum);

/// <summary>Küçültülmüş logo önbelleği — CachedPublicTenantResolver'daki gibi DEDİKE MemoryCache (paylaşımlı
/// IMemoryCache'e SizeLimit koymak diğer tüketicileri etkilerdi). Boyut bayt cinsinden, üst sınır 16 MB.
///
/// <para><b>Tek uçuş:</b> önbellek kaçırmada aynı firma+sürüm için gelen eşzamanlı istekler TEK okuma + TEK
/// çözme paylaşır (anahtar başına <see cref="Lazy{T}"/>). Eskiden her eşzamanlı istek logoyu ayrı ayrı çözüyordu —
/// dev ölçülü bir logoyla 8 anonim istek belleği 562 MB'tan 1,8 GB'a çıkarıyordu.</para>
///
/// <para><b>Bütçe:</b> ölçüleri <see cref="ImageProcessing.FitsWebLogoBudget(byte[])"/> dışındaki logo ÇÖZÜLMEZ ve
/// servis EDİLMEZ (tarayıcıya da bomba gönderilmez); sonuç negatif olarak önbelleğe yazılır, 1 MB'lık bayt her
/// istekte yeniden okunmaz.</para></summary>
public sealed class WebLogoCache(Func<byte[], byte[]?>? resize = null, Func<byte[], bool>? fitsBudget = null)
    : IDisposable
{
    private static readonly WebLogo Unservable = new([], "", "");

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 16 * 1024 * 1024 });
    private readonly ConcurrentDictionary<(Guid, string), Lazy<Task<WebLogo?>>> _inflight = new();
    private readonly Func<byte[], byte[]?> _resize = resize ?? (b => ImageProcessing.TryCreateWebLogo(b));
    private readonly Func<byte[], bool> _fitsBudget = fitsBudget ?? ImageProcessing.FitsWebLogoBudget;

    /// <summary><paramref name="surum"/> için web logosu; önbellekte yoksa <paramref name="load"/> ile baytlar
    /// okunur. Logo yoksa ya da servis edilemezse null.</summary>
    public async Task<WebLogo?> GetOrCreateAsync(Guid tenantId, string surum,
        Func<CancellationToken, Task<PublicLogo?>> load)
    {
        var key = (tenantId, surum);
        if (TryGet(key, out var hit)) return hit;

        var flight = _inflight.GetOrAdd(key, k => new Lazy<Task<WebLogo?>>(() => BuildAsync(k.Item1, k, load)));
        try { return await flight.Value; }
        finally { _inflight.TryRemove(new KeyValuePair<(Guid, string), Lazy<Task<WebLogo?>>>(key, flight)); }
    }

    private async Task<WebLogo?> BuildAsync(Guid tenantId, (Guid, string) key,
        Func<CancellationToken, Task<PublicLogo?>> load)
    {
        // Yarış: önceki uçuş biz sözlüğe girmeden hemen önce bitip önbelleğe yazmış olabilir.
        if (TryGet(key, out var hit)) return hit;

        // Paylaşılan iş: ilk isteğin iptali bekleyen diğer istekleri düşürmesin.
        var logo = await load(CancellationToken.None);
        if (logo is null) return null;

        WebLogo entry;
        if (!_fitsBudget(logo.Bytes))
            entry = Unservable;
        else
        {
            // Küçültülen logo daima PNG. Küçültülemezse (zaten küçük / çözülemedi) özgün dosya; türü İÇERİKTEN —
            // eski kayıtlarda JPEG logo olabilir (PNG zorunluluğu sonradan geldi).
            var resized = _resize(logo.Bytes);
            entry = resized is not null
                ? new WebLogo(resized, "image/png", logo.Surum)
                : new WebLogo(logo.Bytes,
                    ImageValidation.Detect(logo.Bytes) == ImageKind.Jpeg ? "image/jpeg" : "image/png", logo.Surum);
        }
        _cache.Set((tenantId, logo.Surum), entry, new MemoryCacheEntryOptions
        {
            Size = Math.Max(1, entry.Bytes.Length),
            SlidingExpiration = TimeSpan.FromHours(6),
        });
        return ReferenceEquals(entry, Unservable) ? null : entry;
    }

    private bool TryGet((Guid, string) key, out WebLogo? logo)
    {
        if (_cache.TryGetValue(key, out WebLogo? hit) && hit is not null)
        {
            logo = ReferenceEquals(hit, Unservable) ? null : hit;
            return true;
        }
        logo = null;
        return false;
    }

    public void Dispose() => _cache.Dispose();
}
