using Microsoft.Extensions.Caching.Memory;

namespace RentACar.PublicSite;

/// <summary>
/// PR-3.5: <see cref="PublicTenantResolver"/>'ı 30sn sabit TTL ile önbelleğe alır — HER istekte 1-2 DB
/// bağlantısı açılmasını önler. Negatif sonuçlar (NotFound/TenantInactive/SiteDisabled) da cache'lenir.
///
/// KRİTİK — negatif cache SINIRSIZ büyümesin diye DEDİKE ve BOYUT-SINIRLI bir MemoryCache: anahtar
/// SALDIRGAN KONTROLÜNDEKİ Host header'ı. Paylaşılan DI IMemoryCache'e SizeLimit koymak, o cache'in
/// TÜM diğer tüketicilerini her Set çağrısında Size vermeye zorlardı (yoksa runtime'da patlar) —
/// bu yüzden bu sınıf kendi MemoryCache örneğine sahip. 10k host ≈ birkaç MB, LRU evict eder; meşru
/// host sayısı tenant sayısı kadardır (tavana asla değmez), saldırgan hostname'ler evict olur.
///
/// SÜREÇ-SINIRI: "Sitemi Aç" RentACar.Web process'inde yazar, bu cache RentACar.PublicSite'ın KENDİ
/// bellek alanında — invalidation süreç sınırını GEÇEMEZ (TenantStatusCache.cs'nin "farklı process'te
/// ~60sn TTL ile en fazla o kadar bayat" ilkesiyle aynı, burada 30sn'e çekildi). Ayarlar ekranında
/// "Siteniz en geç 1 dakika içinde yayında" notu bu yüzden var.
/// </summary>
public sealed class CachedPublicTenantResolver(IPublicTenantResolver inner) : IPublicTenantResolver, IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    public async Task<PublicTenantResult> ResolveAsync(string host, CancellationToken ct = default)
    {
        var key = "pts:" + host.ToLowerInvariant();
        if (_cache.TryGetValue(key, out PublicTenantResult? cached) && cached is not null) return cached;

        var result = await inner.ResolveAsync(host, ct);
        _cache.Set(key, result, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Ttl });
        return result;
    }

    public void Dispose() => _cache.Dispose();
}
