using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Infrastructure.Identity;

/// <summary>Oturum doğrulamasının okuduğu güncel kullanıcı durumu (Users satırı).</summary>
public sealed record UserSessionState(string Stamp, bool Active);

/// <summary>
/// Güvenlik F1 — çerez oturumunun her istekte karşılaştırdığı oturum damgasının kısa ömürlü önbelleği. Users platform
/// tablosudur (SELECT GUC'suz açık, login bootstrap ile aynı yol) → ham, tenant'sız bağlamla okunur; satır kimliği VE
/// kiracı birlikte eşlenir. Aynı süreçte değişiklik <see cref="Invalidate"/> ile ANINDA görünür (UserRepository
/// yazımdan sonra çağırır); farklı süreçte (Web↔Api) en çok <see cref="Ttl"/> kadar bayat kalabilir.
/// </summary>
public sealed class UserSessionStateCache(IMemoryCache cache, IServiceProvider services)
{
    /// <summary>Güvenlik L1: çok süreçli kurulumda (ikinci Web örneği, Web↔Api) bayatlık penceresi en çok bu kadar.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);
    private static string Key(Guid userId) => $"user-session:{userId}";

    /// <summary>Damga değeri: null (hiç değişmemiş eski satır) boş metin sayılır — girişte de aynı kuralla yazılır.</summary>
    public static string Normalize(string? stamp) => stamp ?? "";

    /// <summary>Yeni rastgele damga (opak; 32 hex).</summary>
    public static string NewStamp() => Guid.NewGuid().ToString("N");

    /// <summary>Satır yoksa (silinmiş/başka kiracı) null — çağıran oturumu düşürür.</summary>
    public async Task<UserSessionState?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(userId), out (Guid Tenant, UserSessionState? State) hit) && hit.Tenant == tenantId)
            return hit.State;
        // Yapılandırma okuma ANINDA çözülür: yazma yolu (UserRepository.Invalidate) yapılandırmasız test/iş
        // host'larında da çalışmalı; okuma yalnız Web çerez doğrulamasında olur.
        var config = (IConfiguration?)services.GetService(typeof(IConfiguration))
            ?? throw new InvalidOperationException("IConfiguration yok (oturum doğrulaması yalnız Web host'unda).");
        var conn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conn).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        var row = await db.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.Id == userId && u.TenantId == tenantId)
            .Select(u => new { u.GuvenlikDamgasi, u.IsActive })
            .FirstOrDefaultAsync(ct);
        var state = row is null ? null : new UserSessionState(Normalize(row.GuvenlikDamgasi), row.IsActive);
        cache.Set(Key(userId), (tenantId, state), Ttl);
        return state;
    }

    public void Invalidate(Guid userId) => cache.Remove(Key(userId));
}
