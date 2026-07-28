using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-0: TenantDomain (public site host→tenant eşlemesi) + TenantSettings.PublicSiteEnabled.
/// TenantDomain PLATFORM tablosu (Tenants/Users gibi) — RLS/tenant-filtre YOK, bilinçli.
/// BAĞIMSIZ ORACLE: Host benzersizliği + varsayılan değer, servis mantığından değil şemadan doğrulanır.
/// </summary>
[Collection("postgres")]
public sealed class TenantDomainTests(PostgresFixture fx)
{
    [Fact]
    public async Task Host_unique_index_ihlali_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        const string sharedHost = "cakisma-testi.rentpro.com";

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.TenantDomains.Add(new TenantDomain { TenantId = t1, Host = sharedHost, Status = TenantDomainStatus.Active });
            await db.SaveChangesAsync();
        }

        await using var db2 = await factory.CreateDbContextAsync();
        db2.TenantDomains.Add(new TenantDomain { TenantId = t2, Host = sharedHost, Status = TenantDomainStatus.Active });
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task TenantDomain_platform_tablosu_tenant_filtresiz_gorunur()
    {
        // Tenant A'nın scope'undan bağlanıp Tenant B için oluşturulmuş bir TenantDomain satırını okuyabilmeli
        // (host çözümlemesi tenant bağlamından ÖNCE olur — filtrelenirse public site hiçbir zaman çalışmaz).
        using var host = new TestHost(fx.AppConnectionString);
        var tenantB = Guid.NewGuid();

        using (var seedScope = host.ScopeFor(tenantB))
        {
            var factory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.TenantDomains.Add(new TenantDomain
            { TenantId = tenantB, Host = "filtresiz-testi.rentpro.com", Status = TenantDomainStatus.Active });
            await db.SaveChangesAsync();
        }

        using var scopeA = host.ScopeFor(Guid.NewGuid()); // farklı (A) tenant bağlamı
        var factoryA = scopeA.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var dbA = await factoryA.CreateDbContextAsync();
        var found = await dbA.TenantDomains.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Host == "filtresiz-testi.rentpro.com");
        Assert.NotNull(found);
        Assert.Equal(tenantB, found!.TenantId);
    }

    [Fact]
    public async Task PublicSiteEnabled_varsayilan_false_ve_yazilabilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var tenantId = scope.ServiceProvider.GetRequiredService<RentACar.Domain.Common.ITenantContext>().TenantId!.Value;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.TenantSettings.Add(new RentACar.Domain.Entities.TenantSettings { TenantId = tenantId });
            await db.SaveChangesAsync();
        }

        await using var read = await factory.CreateDbContextAsync();
        var s = await read.TenantSettings.AsNoTracking().FirstAsync();
        Assert.False(s.PublicSiteEnabled);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var s2 = await db.TenantSettings.FirstAsync();
            s2.PublicSiteEnabled = true;
            await db.SaveChangesAsync();
        }
        await using var read2 = await factory.CreateDbContextAsync();
        Assert.True((await read2.TenantSettings.AsNoTracking().FirstAsync()).PublicSiteEnabled);
    }
}
