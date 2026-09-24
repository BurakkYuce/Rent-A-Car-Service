using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.PublicSite;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-5: Caddy `on_demand_tls` ask-endpoint'i + özel domain doğrulama (subdomain'lerin de bugüne kadar
/// TLS'siz olduğu boşluğu kapatan PR). BAĞIMSIZ ORACLE + GÜVENLİK-KRİTİK: B tenant'ı A'nın host'unu
/// çalamaz (unique index), Pending+Custom host kendi kendini doğrular.
/// </summary>
[Collection("postgres")]
public sealed class TenantDomainVerificationTests(PostgresFixture fx)
{
    private IConfiguration Cfg() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Default"] = fx.AppConnectionString,
    }).Build();

    private AppDbContext Owner() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
        NullTenantContext.Instance, NullCurrentUser.Instance);

    private async Task<Guid> SeedTenantAsync()
    {
        await using var db = Owner();
        // PR-12: modül AÇIK — bu dosyanın konusu domain doğrulaması, lisans değil
        // (lisans kapısı WebSitesiModuluTests'te).
        var t = new Tenant
        {
            Code = "tdv" + Guid.NewGuid().ToString("N")[..10], Name = "TDV",
            IsActive = true, WebSitesiModulu = true,
        };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private async Task SetPublicSiteEnabledAsync(TestHost host, Guid tenantId, bool enabled = true)
    {
        using var scope = host.ScopeFor(tenantId);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.TenantSettings.Add(new RentACar.Domain.Entities.TenantSettings { TenantId = tenantId, PublicSiteEnabled = enabled });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExistsAsync_bilinen_host_true_bilinmeyen_false()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = await SeedTenantAsync();
        var domainHost = "ask-" + Guid.NewGuid().ToString("N") + ".rentpro.com";

        using (var scope = host.ScopeFor(tenantId))
        {
            var domains = scope.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
            await domains.AddCustomAsync(tenantId, domainHost);
        }

        using var readScope = host.ScopeFor(tenantId);
        var repo = readScope.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
        Assert.True(await repo.ExistsAsync(domainHost));
        Assert.False(await repo.ExistsAsync("bilinmeyen-" + Guid.NewGuid().ToString("N") + ".com"));
    }

    [Fact]
    public async Task AddCustomAsync_idempotent_ayni_host_tekrar_eklenirse_yeni_satir_yaratmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = await SeedTenantAsync();
        var domainHost = "idem-" + Guid.NewGuid().ToString("N") + ".com";

        using var scope = host.ScopeFor(tenantId);
        var domains = scope.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
        var first = await domains.AddCustomAsync(tenantId, domainHost);
        var second = await domains.AddCustomAsync(tenantId, domainHost);

        Assert.Equal(first.Id, second.Id);
        var list = await domains.ListAsync(tenantId);
        Assert.Single(list, d => d.Host == domainHost);
    }

    [Fact]
    public async Task Tenant_basina_ucuncu_pending_domain_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = await SeedTenantAsync();

        using var scope = host.ScopeFor(tenantId);
        var domains = scope.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
        await domains.AddCustomAsync(tenantId, "d1-" + Guid.NewGuid().ToString("N") + ".com");
        await domains.AddCustomAsync(tenantId, "d2-" + Guid.NewGuid().ToString("N") + ".com");

        await Assert.ThrowsAsync<ValidationException>(
            () => domains.AddCustomAsync(tenantId, "d3-" + Guid.NewGuid().ToString("N") + ".com"));
    }

    [Fact]
    public async Task ExpireOldPendingCustomDomainsAsync_48_saati_gecen_pending_failed_olur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = await SeedTenantAsync();
        var domainHost = "expire-" + Guid.NewGuid().ToString("N") + ".com";

        Guid domainId;
        using (var scope = host.ScopeFor(tenantId))
        {
            var domains = scope.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
            var d = await domains.AddCustomAsync(tenantId, domainHost);
            domainId = d.Id;

            // Elle geçmişe çekilmiş CreatedAtUtc — bağımsız oracle: 49 saat önce oluşturulmuş gibi.
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            await db.TenantDomains.Where(x => x.Id == domainId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAtUtc, DateTimeOffset.UtcNow.AddHours(-49)));
        }

        using (var scope = host.ScopeFor(tenantId))
        {
            var domains = scope.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
            var expired = await domains.ExpireOldPendingCustomDomainsAsync(DateTimeOffset.UtcNow.AddHours(-48));
            Assert.Equal(1, expired);

            var list = await domains.ListAsync(tenantId);
            Assert.Equal(TenantDomainStatus.Failed, list.Single(d => d.Id == domainId).Status);
        }
    }

    [Fact]
    public async Task Resolver_pending_custom_host_COZULMEZ_ve_kendiliginden_active_olmaz()
    {
        // F11.1b güvenlik M6: bekleyen alan adı yalnız DNS TXT doğrulamasıyla etkinleşir; ilk istek sahiplik kanıtı DEĞİL.
        var tenantId = await SeedTenantAsync();
        var domainHost = "pendingno-" + Guid.NewGuid().ToString("N") + ".com";
        using (var host = new TestHost(fx.AppConnectionString))
        {
            using var scope = host.ScopeFor(tenantId);
            var domains = scope.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
            var d = await domains.AddCustomAsync(tenantId, domainHost);
            Assert.StartsWith("racar-", d.VerificationToken);
            await SetPublicSiteEnabledAsync(host, tenantId);
        }

        var result = await new PublicTenantResolver(Cfg()).ResolveAsync(domainHost);
        Assert.Equal(PublicTenantResolution.NotFound, result.Kind);
        await using var owner = Owner();
        var row = await owner.TenantDomains.AsNoTracking().SingleAsync(d => d.Host == domainHost);
        Assert.Equal(TenantDomainStatus.PendingVerification, row.Status);
        Assert.Null(row.VerifiedAtUtc);
    }

    [Fact]
    public async Task Active_custom_domain_active_subdomain_ile_ayni_davranir()
    {
        var tenantId = await SeedTenantAsync();
        var domainHost = "activecustom-" + Guid.NewGuid().ToString("N") + ".com";

        using (var host = new TestHost(fx.AppConnectionString))
        {
            using var scope = host.ScopeFor(tenantId);
            var domains = scope.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
            var d = await domains.AddCustomAsync(tenantId, domainHost);
            await domains.MarkVerifiedAsync(d.Id); // Active'e elle çevir (daha önce doğrulanmış gibi)
            await SetPublicSiteEnabledAsync(host, tenantId);
        }

        var resolver = new PublicTenantResolver(Cfg());
        var result = await resolver.ResolveAsync(domainHost);
        Assert.Equal(PublicTenantResolution.Found, result.Kind);
        Assert.Equal(tenantId, result.TenantId);
    }

    [Fact]
    public async Task B_tenanti_a_nin_hostunu_calamaz_unique_index_korur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = await SeedTenantAsync();
        var tenantB = await SeedTenantAsync();
        var sharedHost = "hijack-" + Guid.NewGuid().ToString("N") + ".com";

        using (var scopeA = host.ScopeFor(tenantA))
        {
            var domainsA = scopeA.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
            await domainsA.AddCustomAsync(tenantA, sharedHost);
        }

        // F11.1b güvenlik M6: A'nın satırı DOĞRULANMIŞ (Active) iken B alamaz; mesaj A'nın varlığını sızdırmaz.
        Guid aRow;
        using (var scopeA = host.ScopeFor(tenantA))
        {
            var domainsA = scopeA.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
            aRow = (await domainsA.ListAsync(tenantA)).Single(d => d.Host == sharedHost).Id;
            await domainsA.MarkVerifiedAsync(aRow);
        }

        using var scopeB = host.ScopeFor(tenantB);
        var domainsB = scopeB.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => domainsB.AddCustomAsync(tenantB, sharedHost));
        Assert.Equal(DomainVerification.CannotAddMessage, ex.Message);
        Assert.DoesNotContain("başka", ex.Message, StringComparison.OrdinalIgnoreCase);

        // B'nin hiçbir satırı oluşmamalı — A'nın satırı DEĞİŞMEDEN kalmalı.
        var listB = await domainsB.ListAsync(tenantB);
        Assert.DoesNotContain(listB, d => d.Host == sharedHost);
        await using var owner = Owner();
        Assert.Equal(tenantA, (await owner.TenantDomains.AsNoTracking().SingleAsync(d => d.Id == aRow)).TenantId);
    }

    [Fact]
    public async Task Dogrulanmamis_kayit_gercek_sahibin_eklemesini_ENGELLEMEZ()
    {
        // F11.1b güvenlik M6: A başkasının alan adını ekleyip bekletirse, gerçek sahip B yine ekler (kendi belirteciyle);
        // A'nın doğrulanmamış kaydı sahiplik değildir.
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = await SeedTenantAsync();
        var tenantB = await SeedTenantAsync();
        var sharedHost = "squat-" + Guid.NewGuid().ToString("N") + ".com";

        string tokenA;
        using (var scopeA = host.ScopeFor(tenantA))
            tokenA = (await scopeA.ServiceProvider.GetRequiredService<ITenantDomainRepository>().AddCustomAsync(tenantA, sharedHost)).VerificationToken!;

        using var scopeB = host.ScopeFor(tenantB);
        var domainsB = scopeB.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
        var rowB = await domainsB.AddCustomAsync(tenantB, sharedHost);
        Assert.Equal(tenantB, rowB.TenantId);
        Assert.Equal(TenantDomainStatus.PendingVerification, rowB.Status);
        Assert.NotEqual(tokenA, rowB.VerificationToken);

        await using var owner = Owner();
        var rows = await owner.TenantDomains.AsNoTracking().Where(d => d.Host == sharedHost).ToListAsync();
        Assert.Equal(tenantB, Assert.Single(rows).TenantId);
        // A'nın eski belirteci B'nin kaydını etkinleştiremez.
        Assert.False(await domainsB.ActivateVerifiedAsync(tenantA, rowB.Id, tokenA));
    }
}
