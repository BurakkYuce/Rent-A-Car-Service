using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Platform;

/// <summary>
/// Tenant.IsActive'in kısa-ömürlü cache'i (anlık kesme için). `Tenants` platform tablosu (RLS yok,
/// racar_app SELECT açık) → app-conn factory ile okunur (query filter yok, GUC önemsiz). Toggle'da
/// <see cref="Invalidate"/> ile aynı-instance ANINDA; aksi halde ~60sn TTL ile en fazla o kadar bayat.
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
