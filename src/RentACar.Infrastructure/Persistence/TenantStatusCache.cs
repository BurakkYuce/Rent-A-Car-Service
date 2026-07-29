using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Tenant'ın platform-taraflı durumunun kısa-ömürlü cache'i: erişim aç/kapa anlık-kesmesi (Web middleware
/// + API JWT OnTokenValidated ORTAK kullanır) + PR-12 "Web Sitesi" modül kapısı. `Tenants` platform
/// tablosu (RLS yok, racar_app SELECT açık) → app-conn factory ile okunur (query filter yok, GUC önemsiz).
/// Aynı process'te toggle <see cref="Invalidate"/> ile ANINDA; farklı process'te (Web↔Api) ~60sn TTL ile
/// en fazla o kadar bayat.
///
/// PR-12: modül bayrağı AYRI cache'e KONMADI — aynı satırdan okunuyor. Ayrı anahtar ikinci bir sorgu ve
/// ikinci bir "invalidate etmeyi unutma" riski demekti ("modülü açtım, menü gelmedi").
/// </summary>
public sealed class TenantStatusCache(IMemoryCache cache, IDbContextFactory<AppDbContext> factory)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private static string Key(Guid id) => $"tenant-status:{id}";

    /// <summary>Bilinmeyen tenant → <c>(false, false)</c> — güvenli varsayılan.</summary>
    private readonly record struct Durum(bool Aktif, bool WebSitesiModulu);

    private async Task<Durum> DurumAsync(Guid tenantId, CancellationToken ct)
    {
        if (cache.TryGetValue(Key(tenantId), out Durum d)) return d;
        await using var db = await factory.CreateDbContextAsync(ct);
        var t = await db.Tenants.AsNoTracking()
            .Where(x => x.Id == tenantId)
            .Select(x => new { x.IsActive, x.WebSitesiModulu })
            .FirstOrDefaultAsync(ct);
        d = new Durum(t?.IsActive ?? false, t?.WebSitesiModulu ?? false);
        cache.Set(Key(tenantId), d, Ttl);
        return d;
    }

    public async Task<bool> IsActiveAsync(Guid tenantId, CancellationToken ct = default)
        => (await DurumAsync(tenantId, ct)).Aktif;

    /// <summary>PR-12: "Web Sitesi" modülü satın alınmış mı. ERP menü kapısı VE <c>/web-sitesi/*</c>
    /// uçlarının SUNUCU-taraflı doğrulaması bunu okur — menü gizlemek yalnız görseldir.</summary>
    public async Task<bool> WebSitesiModuluAsync(Guid tenantId, CancellationToken ct = default)
        => (await DurumAsync(tenantId, ct)).WebSitesiModulu;

    public void Invalidate(Guid tenantId) => cache.Remove(Key(tenantId));
}
