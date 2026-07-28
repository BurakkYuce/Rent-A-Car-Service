using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;

namespace RentACar.PublicSite;

public enum PublicTenantResolution { NotFound, TenantInactive, SiteDisabled, Found }

public sealed record PublicTenantResult(PublicTenantResolution Kind, Guid? TenantId = null);

/// <summary>PR-2/PR-3.5: host→tenant çözümleme sözleşmesi — ayrı arayüz, <see cref="CachedPublicTenantResolver"/>
/// decorator'ının (ve testlerin sayaçlı sahtelerinin) etrafını sarabilmesi için.</summary>
public interface IPublicTenantResolver
{
    Task<PublicTenantResult> ResolveAsync(string host, CancellationToken ct = default);
}

/// <summary>
/// Host→tenant çözümleme (PR-2). HTTP/middleware'den BAĞIMSIZ düz servis — <see cref="TenantHostResolutionMiddleware"/>
/// ince bir sarmalayıcıdır (test edilebilirlik: CalendarFeedService.cs deseni, doğrudan çağrılabilir).
///
/// İKİ FAZLI okuma ŞART: <see cref="TenantDomain"/>/<see cref="Tenant"/> platform tabloları (RLS yok) —
/// NullTenantContext ile okunur. <see cref="RentACar.Domain.Entities.TenantSettings"/> ise ITenantOwned
/// (RLS'e tabi) — bu raw context (DI dışı, interceptor'sız) GUC'u kimse otomatik set etmez, bu yüzden
/// SystemTenantContext + TenantGuc.OpenAsync ile AYRI bir context üzerinden okunur (TenantGuc.cs'in kendi
/// uyarısı: sıralama şart, aksi halde RLS sessizce 0 satır döner).
/// </summary>
public sealed class PublicTenantResolver(IConfiguration config) : IPublicTenantResolver
{
    public async Task<PublicTenantResult> ResolveAsync(string host, CancellationToken ct = default)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(config.GetConnectionString("Default")!).Options;

        Guid tenantId;
        await using (var db0 = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var row = await db0.TenantDomains.AsNoTracking().IgnoreQueryFilters()
                .Where(d => d.Host == host.ToLowerInvariant() && d.Status == TenantDomainStatus.Active)
                .Select(d => d.TenantId).FirstOrDefaultAsync(ct);
            if (row == Guid.Empty) return new(PublicTenantResolution.NotFound);

            var active = await db0.Tenants.AsNoTracking()
                .Where(x => x.Id == row).Select(x => x.IsActive).FirstOrDefaultAsync(ct);
            if (!active) return new(PublicTenantResolution.TenantInactive);
            tenantId = row;
        }

        var sys = new SystemTenantContext { TenantId = tenantId };
        await using var db = new AppDbContext(options, sys, sys);
        await TenantGuc.OpenAsync(db, tenantId, ct);
        var enabled = await db.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId).Select(s => s.PublicSiteEnabled).FirstOrDefaultAsync(ct);
        return enabled ? new(PublicTenantResolution.Found, tenantId) : new(PublicTenantResolution.SiteDisabled);
    }
}
