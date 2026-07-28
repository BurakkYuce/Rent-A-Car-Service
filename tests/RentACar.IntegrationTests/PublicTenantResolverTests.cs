using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.PublicSite;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-2: host→tenant çözümleme (public-site). GÜVENLİK-KRİTİK — CalendarFeedTests.cs deseniyle
/// BAĞIMSIZ ORACLE: A'nın host'u A'nın tenant'ını verir, B'ninkini ASLA vermez; bilinmeyen host/kapalı
/// tenant/kapalı site hep aynı sonuca (404'e eşlenecek NotFound-benzeri) düşer — hiçbiri varsayılan
/// tenant'a sızmaz. Servis doğrudan çağrılır (middleware/HTTP katmanı yok — CalendarFeedTests.cs deseni).
/// </summary>
[Collection("postgres")]
public sealed class PublicTenantResolverTests(PostgresFixture fx)
{
    private IConfiguration Cfg() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Default"] = fx.AppConnectionString,
    }).Build();

    private AppDbContext Owner() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
        NullTenantContext.Instance, NullCurrentUser.Instance);

    private async Task<Guid> SeedTenantAsync(bool tenantActive = true)
    {
        await using var db = Owner();
        var t = new Tenant { Code = "ps" + Guid.NewGuid().ToString("N")[..10], Name = "PS", IsActive = tenantActive };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private async Task AddDomainAsync(Guid tenantId, string host, TenantDomainStatus status = TenantDomainStatus.Active)
    {
        using var host2 = new TestHost(fx.AppConnectionString);
        using var scope = host2.ScopeFor(tenantId);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.TenantDomains.Add(new TenantDomain { TenantId = tenantId, Host = host, Kind = TenantDomainKind.Subdomain, Status = status });
        await db.SaveChangesAsync();
    }

    private async Task SetPublicSiteEnabledAsync(Guid tenantId, bool enabled)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.TenantSettings.Add(new RentACar.Domain.Entities.TenantSettings { TenantId = tenantId, PublicSiteEnabled = enabled });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Aninin_hostu_ancak_a_tenantini_verir_b_ye_asla_sizmaz()
    {
        var tenantA = await SeedTenantAsync();
        var tenantB = await SeedTenantAsync();
        var hostA = "a-" + Guid.NewGuid().ToString("N") + ".rentpro.com";
        var hostB = "b-" + Guid.NewGuid().ToString("N") + ".rentpro.com";
        await AddDomainAsync(tenantA, hostA);
        await AddDomainAsync(tenantB, hostB);
        await SetPublicSiteEnabledAsync(tenantA, true);
        await SetPublicSiteEnabledAsync(tenantB, true);

        var resolver = new PublicTenantResolver(Cfg());

        var resultA = await resolver.ResolveAsync(hostA);
        Assert.Equal(PublicTenantResolution.Found, resultA.Kind);
        Assert.Equal(tenantA, resultA.TenantId);
        Assert.NotEqual(tenantB, resultA.TenantId); // çapraz-tenant sızması yok

        var resultB = await resolver.ResolveAsync(hostB);
        Assert.Equal(PublicTenantResolution.Found, resultB.Kind);
        Assert.Equal(tenantB, resultB.TenantId);
    }

    [Fact]
    public async Task Bilinmeyen_host_notfound_doner_varsayilan_tenanta_dusmez()
    {
        var resolver = new PublicTenantResolver(Cfg());
        var result = await resolver.ResolveAsync("bilinmeyen-" + Guid.NewGuid().ToString("N") + ".rentpro.com");
        Assert.Equal(PublicTenantResolution.NotFound, result.Kind);
        Assert.Null(result.TenantId);
    }

    [Fact]
    public async Task Kapali_tenant_tenantinactive_doner()
    {
        var tenantId = await SeedTenantAsync(tenantActive: false);
        var host = "kapali-" + Guid.NewGuid().ToString("N") + ".rentpro.com";
        await AddDomainAsync(tenantId, host);
        await SetPublicSiteEnabledAsync(tenantId, true);

        var resolver = new PublicTenantResolver(Cfg());
        var result = await resolver.ResolveAsync(host);
        Assert.Equal(PublicTenantResolution.TenantInactive, result.Kind);
    }

    [Fact]
    public async Task Site_kapaliysa_sitedisabled_doner()
    {
        var tenantId = await SeedTenantAsync();
        var host = "sitekapali-" + Guid.NewGuid().ToString("N") + ".rentpro.com";
        await AddDomainAsync(tenantId, host);
        await SetPublicSiteEnabledAsync(tenantId, false);

        var resolver = new PublicTenantResolver(Cfg());
        var result = await resolver.ResolveAsync(host);
        Assert.Equal(PublicTenantResolution.SiteDisabled, result.Kind);
    }

    [Fact]
    public async Task PendingVerification_domain_active_sayilmaz()
    {
        var tenantId = await SeedTenantAsync();
        var host = "bekleyen-" + Guid.NewGuid().ToString("N") + ".rentpro.com";
        await AddDomainAsync(tenantId, host, TenantDomainStatus.PendingVerification);
        await SetPublicSiteEnabledAsync(tenantId, true);

        var resolver = new PublicTenantResolver(Cfg());
        var result = await resolver.ResolveAsync(host);
        Assert.Equal(PublicTenantResolution.NotFound, result.Kind);
    }

    [Fact]
    public async Task OpenPublicSiteAsync_sonrasi_resolver_found_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        Guid tenantId;
        await using (var owner = Owner())
        {
            var t = new Tenant { Code = "ps-open-" + Guid.NewGuid().ToString("N")[..8], Name = "PS Open" };
            owner.Tenants.Add(t);
            await owner.SaveChangesAsync();
            tenantId = t.Id;
        }

        using (var scope = host.ScopeFor(tenantId))
        {
            var svc = scope.ServiceProvider.GetRequiredService<RentACar.Application.TenantSettings.TenantSettingsService>();
            await svc.OpenPublicSiteAsync();
        }

        string expectedHost;
        await using (var owner = Owner())
        {
            expectedHost = await owner.Tenants.Where(t => t.Id == tenantId).Select(t => t.Code + ".rentpro.com").FirstAsync();
        }

        var resolver = new PublicTenantResolver(Cfg());
        var result = await resolver.ResolveAsync(expectedHost);
        Assert.Equal(PublicTenantResolution.Found, result.Kind);
        Assert.Equal(tenantId, result.TenantId);
    }
}
