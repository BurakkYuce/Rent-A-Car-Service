using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Identity;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.PublicSite;
using RentACar.Web.Platform;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-12 — "Web Sitesi" modül lisansı. Bu bir SATIN ALMA kilididir: yalnız platform konsolundan
/// açılır, tenant kendi ERP'sinden açamaz.
///
/// Bağımsız oracle: beklenenler senaryodan kurulur (modülü açtım/kapattım → ne olmalı), servisin
/// kendi mantığından türetilmez. En kritik iki test:
/// <list type="bullet">
///   <item><b>Varsayılan KAPALI</b> — yeni tenant modülü satın almış SAYILMAZ.</item>
///   <item><b>Tenant kendi açamaz</b> — `TenantSettings` yolu (müşteriye `ManageUsers` ile açık)
///     bu bayrağa DOKUNAMAZ; aksi halde kilit kilit olmazdı.</item>
/// </list>
/// </summary>
[Collection("postgres")]
public sealed class WebSitesiModuluTests(PostgresFixture fx)
{
    private IConfiguration PublicCfg() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:Default"] = fx.AppConnectionString }).Build();

    private AppDbContext Owner() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
        NullTenantContext.Instance, NullCurrentUser.Instance);

    private (PlatformAdminService Svc, TenantStatusCache Cache) BuildPlatform()
    {
        var appOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options;
        var factory = new ScopedAppDbContextFactory(appOptions, NullTenantContext.Instance, NullCurrentUser.Instance);
        var cache = new TenantStatusCache(new MemoryCache(new MemoryCacheOptions()), factory);
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Migrator"] = fx.OwnerConnectionString }).Build();
        return (new PlatformAdminService(config, new AspNetPasswordHasher(), cache,
            NullLogger<PlatformAdminService>.Instance), cache);
    }

    private async Task<Guid> SeedTenantAsync(bool module = false)
    {
        await using var db = Owner();
        var t = new Tenant
        {
            Code = "wm" + Guid.NewGuid().ToString("N")[..10], Name = "WM",
            IsActive = true, WebSitesiModulu = module,
        };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private async Task SeedSiteAsync(Guid tenantId, string host)
    {
        using var testHost = new TestHost(fx.AppConnectionString);
        using var scope = testHost.ScopeFor(tenantId);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.TenantDomains.Add(new TenantDomain
        {
            TenantId = tenantId, Host = host,
            Kind = TenantDomainKind.Subdomain, Status = TenantDomainStatus.Active,
        });
        db.TenantSettings.Add(new TenantSettings { TenantId = tenantId, PublicSiteEnabled = true });
        await db.SaveChangesAsync();
    }

    // ---- Varsayılan ----

    [Fact]
    public async Task Yeni_tenantta_modul_KAPALIDIR()
    {
        var (svc, _) = BuildPlatform();
        var code = "wm" + Guid.NewGuid().ToString("N")[..10];
        await svc.CreateTenantAsync(code, "Modül Testi", "admin", "sifre123", "op");
        var id = (await svc.ListTenantsAsync()).First(t => t.Code == code).Id;

        // Satın alınmadan hiçbir şey açılmaz — kilidin varsayılanı budur.
        Assert.False((await svc.GetTenantAsync(id))!.WebSitesiModulu);
    }

    // ---- Halka açık site kapısı ----

    [Fact]
    public async Task Modul_kapaliyken_site_yayinda_olsa_bile_ACILMAZ()
    {
        var tenantId = await SeedTenantAsync(module: false);
        var host = "modulsuz-" + Guid.NewGuid().ToString("N") + ".rentpro.com";
        await SeedSiteAsync(tenantId, host); // tenant KENDİ "Sitemi Aç"ını açmış

        var result = await new PublicTenantResolver(PublicCfg()).ResolveAsync(host);

        // İki kademe AYRI: tenant tercihi açık ama satın alma yok → site yok.
        Assert.Equal(PublicTenantResolution.ModulKapali, result.Kind);
    }

    [Fact]
    public async Task Modul_acikken_site_cozulur()
    {
        var tenantId = await SeedTenantAsync(module: true);
        var host = "modullu-" + Guid.NewGuid().ToString("N") + ".rentpro.com";
        await SeedSiteAsync(tenantId, host);

        var result = await new PublicTenantResolver(PublicCfg()).ResolveAsync(host);

        Assert.Equal(PublicTenantResolution.Found, result.Kind);
        Assert.Equal(tenantId, result.TenantId);
    }

    [Fact]
    public async Task Modul_acik_ama_site_kapaliysa_yine_ACILMAZ()
    {
        var tenantId = await SeedTenantAsync(module: true);
        var host = "tercihkapali-" + Guid.NewGuid().ToString("N") + ".rentpro.com";
        using (var testHost = new TestHost(fx.AppConnectionString))
        using (var scope = testHost.ScopeFor(tenantId))
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.TenantDomains.Add(new TenantDomain
            {
                TenantId = tenantId, Host = host,
                Kind = TenantDomainKind.Subdomain, Status = TenantDomainStatus.Active,
            });
            db.TenantSettings.Add(new TenantSettings { TenantId = tenantId, PublicSiteEnabled = false });
            await db.SaveChangesAsync();
        }

        // Satın alma var, tenant tercihi yok → site yok. Kademeler İKİ YÖNLÜ bağımsız.
        Assert.Equal(PublicTenantResolution.SiteDisabled,
            (await new PublicTenantResolver(PublicCfg()).ResolveAsync(host)).Kind);
    }

    // ---- Platform yazma yolu + cache ----

    [Fact]
    public async Task Platform_modulu_acar_ve_cache_ANINDA_tazelenir()
    {
        var (svc, cache) = BuildPlatform();
        var tenantId = await SeedTenantAsync(module: false);

        Assert.False(await cache.WebsiteModuleAsync(tenantId)); // cache'i ISIT (kapalı değerle)
        await svc.SetWebsiteModuleAsync(tenantId, true, "op");

        // Invalidate çağrılmasaydı TTL (60 sn) boyunca "modülü açtım, menü gelmedi" yaşanırdı.
        Assert.True(await cache.WebsiteModuleAsync(tenantId));
    }

    [Fact]
    public async Task Platform_modulu_kapatir_veri_SILINMEZ()
    {
        var (svc, cache) = BuildPlatform();
        var tenantId = await SeedTenantAsync(module: true);

        await svc.SetWebsiteModuleAsync(tenantId, false, "op");

        Assert.False(await cache.WebsiteModuleAsync(tenantId));
        await using var db = Owner();
        Assert.NotNull(await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId)); // tenant duruyor
    }

    // ---- KİLİDİN KENDİSİ: tenant kendi açamaz ----

    [Fact]
    public async Task Tenant_kendi_ayarlarindan_modulu_ACAMAZ()
    {
        var tenantId = await SeedTenantAsync(module: false);
        using var testHost = new TestHost(fx.AppConnectionString);
        using var scope = testHost.ScopeFor(tenantId); // Admin rolü — tenant'ın en yetkilisi

        // Tenant'ın kendi ayar yolu (ManageUsers ile müşteriye AÇIK) bu bayrağa dokunamaz:
        // bayrak `Tenant` PLATFORM tablosunda, `TenantSettings`'te değil.
        await scope.ServiceProvider.GetRequiredService<TenantSettingsService>()
            .SaveAsync(new TenantSettingsModel { FirmaUnvan = "Deneme A.Ş." });

        await using var db = Owner();
        var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == tenantId);
        Assert.False(t.WebSitesiModulu); // kilit KİLİT
    }

    [Fact]
    public async Task Modul_bayragi_tenant_izoledir()
    {
        var (svc, cache) = BuildPlatform();
        var t1 = await SeedTenantAsync(module: false);
        var t2 = await SeedTenantAsync(module: false);

        await svc.SetWebsiteModuleAsync(t1, true, "op");

        Assert.True(await cache.WebsiteModuleAsync(t1));
        Assert.False(await cache.WebsiteModuleAsync(t2)); // T1'in lisansı T2'ye SIZMAZ
    }
}
