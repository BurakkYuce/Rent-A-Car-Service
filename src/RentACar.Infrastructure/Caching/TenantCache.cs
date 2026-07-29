using Microsoft.Extensions.Caching.Memory;
using RentACar.Application.Common;
using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Caching;

/// <summary>
/// <see cref="ITenantCache"/> — singleton IMemoryCache üzerine tenant-kapsamlı anahtar (izolasyon). Scoped:
/// istek başına geçerli tenant'ı yakalar. 10 dk TTL güvenlik ağı; asıl tazeleme yazımda Invalidate ile.
/// </summary>
public sealed class TenantCache(IMemoryCache cache, ITenantContext tenant) : ITenantCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private string Key(string key) => $"tc:{tenant.TenantId ?? Guid.Empty}:{key}";

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, CancellationToken ct = default, TimeSpan? ttl = null)
    {
        var full = Key(key);
        if (cache.TryGetValue(full, out T? cached) && cached is not null)
            return cached;
        var value = await factory();
        cache.Set(full, value, ttl ?? Ttl); // PR-10: anahtar-bazlı TTL (varsayılan sabit korunur)
        return value;
    }

    public void Invalidate(string key) => cache.Remove(Key(key));
}
