using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RentACar.Infrastructure.Persistence;

namespace RentACar.PublicSite;

/// <summary>
/// PR-5: Caddy `on_demand_tls` ask-endpoint'i için host→bilinir-mi? cache'i. `CachedPublicTenantResolver`
/// ile AYNI iki karar: (a) dedike, boyut-sınırlı `MemoryCache` — saldırgan-kontrollü Host anahtarı bilinmeyen
/// host'lara bot trafiğiyle sınırsız büyümesin (10k host ≈ birkaç MB, LRU evict eder); (b) bu sınıf Singleton
/// olduğu için DI'nin scoped `IDbContextFactory`/`TenantConnectionInterceptor` zincirini KULLANMAZ — tıpkı
/// `PublicTenantResolver`'ın kendi ham `AppDbContext` kurma deseni gibi (Singleton'dan scoped bağımlılık
/// güvenle enjekte edilemez). `TenantDomain` platform tablosu (RLS yok) → `NullTenantContext` yeterli.
/// </summary>
public sealed class DomainAskCache(IConfiguration config) : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    public async Task<bool> ExistsAsync(string host, CancellationToken ct = default)
    {
        var key = host.ToLowerInvariant();
        if (_cache.TryGetValue(key, out bool cached)) return cached;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(config.GetConnectionString("Default")!).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        var exists = await db.TenantDomains.AsNoTracking().AnyAsync(d => d.Host == key, ct);

        _cache.Set(key, exists, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Ttl });
        return exists;
    }

    public void Dispose() => _cache.Dispose();
}
