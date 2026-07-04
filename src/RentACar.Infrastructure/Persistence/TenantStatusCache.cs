using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Tenant.IsActive'in kısa-ömürlü cache'i (erişim aç/kapa anlık-kesmesi için — Web middleware + API JWT
/// OnTokenValidated ORTAK kullanır). `Tenants` platform tablosu (RLS yok, racar_app SELECT açık) → app-conn
/// factory ile okunur (query filter yok, GUC önemsiz). Aynı process'te toggle <see cref="Invalidate"/> ile
/// ANINDA; farklı process'te (Web↔Api) ~60sn TTL ile en fazla o kadar bayat.
/// </summary>
public sealed class TenantStatusCache(IMemoryCache cache, IDbContextFactory<AppDbContext> factory)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private static string Key(Guid id) => $"tenant-active:{id}";

    public async Task<bool> IsActiveAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(tenantId), out bool active)) return active;
        await using var db = await factory.CreateDbContextAsync(ct);
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        active = t?.IsActive ?? false; // bilinmeyen tenant → pasif (güvenli varsayılan)
        cache.Set(Key(tenantId), active, Ttl);
        return active;
    }

    public void Invalidate(Guid tenantId) => cache.Remove(Key(tenantId));
}
