using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;

namespace RentACar.PublicSite;

/// <summary>PR-12 <c>ModulKapali</c>: "Web Sitesi" modülü satın alınmamış. Ziyaretçiye
/// <c>SiteDisabled</c>/<c>NotFound</c> ile AYNI 404 döner (bilgi sızdırmama kararı korunur);
/// ayrı değer olmasının sebebi log/test ayırt edilebilirliğidir.</summary>
public enum PublicTenantResolution { NotFound, TenantInactive, SiteDisabled, ModulKapali, Found }

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
///
/// PR-5: `PendingVerification`+`Custom` host'lar da GEÇERLİ sayılır (Active ile birlikte) — aksi halde
/// Caddy `on_demand_tls`'in ACME challenge'ı için attığı İLK gerçek istek 404 alır, kendi kendini asla
/// doğrulayamaz. Nihai sonuç `Found` ise (tenant aktif + site açık) bu, Let's Encrypt'in HTTP-01 challenge'ı
/// zaten DNS sahipliğini kriptografik olarak kanıtladığı anlamına gelir — o an satır `Active`+`VerifiedAtUtc`
/// olarak işaretlenir (kendi-kendini-doğrulama, ayrı bir DNS-TXT sistemi GEREKMEZ).
/// </summary>
public sealed class PublicTenantResolver(IConfiguration config) : IPublicTenantResolver
{
    public async Task<PublicTenantResult> ResolveAsync(string host, CancellationToken ct = default)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(config.GetConnectionString("Default")!).Options;
        var h = host.ToLowerInvariant();

        Guid tenantId;
        Guid? pendingDomainId = null;
        await using (var db0 = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var row = await db0.TenantDomains.AsNoTracking()
                .Where(d => d.Host == h && (d.Status == TenantDomainStatus.Active
                    || (d.Kind == TenantDomainKind.Custom && d.Status == TenantDomainStatus.PendingVerification)))
                .Select(d => new { d.Id, d.TenantId, d.Status })
                .FirstOrDefaultAsync(ct);
            if (row is null) return new(PublicTenantResolution.NotFound);

            // PR-12: modül bayrağı AYNI satırdan okunur → ek sorgu YOK. `Tenant` platform tablosu
            // olduğu için bu faz-1 (NullTenantContext) okumasına doğal olarak sığar.
            var t = await db0.Tenants.AsNoTracking()
                .Where(x => x.Id == row.TenantId)
                .Select(x => new { x.IsActive, x.WebSitesiModulu })
                .FirstOrDefaultAsync(ct);
            if (t is null || !t.IsActive) return new(PublicTenantResolution.TenantInactive);
            // Modül satın alınmamışsa site YOK. Tenant kendi "Sitemi Aç"ı açmış olsa bile geçerli —
            // iki kademe AYRI: satın alma (platform) + tercih (tenant).
            if (!t.WebSitesiModulu) return new(PublicTenantResolution.ModulKapali);

            tenantId = row.TenantId;
            if (row.Status == TenantDomainStatus.PendingVerification) pendingDomainId = row.Id;
        }

        var sys = new SystemTenantContext { TenantId = tenantId };
        await using var db = new AppDbContext(options, sys, sys);
        await TenantGuc.OpenAsync(db, tenantId, ct);
        var enabled = await db.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId).Select(s => s.PublicSiteEnabled).FirstOrDefaultAsync(ct);
        if (!enabled) return new(PublicTenantResolution.SiteDisabled);

        if (pendingDomainId is { } id)
        {
            await db.TenantDomains.Where(d => d.Id == id && d.Status == TenantDomainStatus.PendingVerification)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, TenantDomainStatus.Active)
                    .SetProperty(d => d.VerifiedAtUtc, DateTimeOffset.UtcNow), ct);
        }

        return new(PublicTenantResolution.Found, tenantId);
    }
}
