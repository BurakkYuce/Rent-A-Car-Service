using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Fleet;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-9: kanonik host seçimi — `canonical` link, `sitemap.xml` ve `robots.txt`'in TEK doğruluk kaynağı.
/// PR-5'in Pending sınırı (tenant başına 2) zamanla birden fazla Active özel domain bırakabildiği için
/// seçim DETERMİNİSTİK olmalı: en eski doğrulanan Active Custom, yoksa subdomain.
/// </summary>
[Collection("postgres")]
public sealed class CanonicalHostTests(PostgresFixture fx)
{
    private AppDbContext Owner() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
        NullTenantContext.Instance, NullCurrentUser.Instance);

    private async Task<Guid> SeedTenantAsync()
    {
        await using var db = Owner();
        var t = new Tenant { Code = "ch" + Guid.NewGuid().ToString("N")[..10], Name = "CH", IsActive = true };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private async Task AddDomainAsync(Guid tenantId, string host, TenantDomainKind kind,
        TenantDomainStatus status, DateTimeOffset? verifiedAt = null)
    {
        await using var db = Owner();
        db.TenantDomains.Add(new TenantDomain
        {
            TenantId = tenantId, Host = host, Kind = kind, Status = status, VerifiedAtUtc = verifiedAt,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string?> CanonicalAsync(TestHost host, Guid tenantId)
    {
        using var scope = host.ScopeFor(tenantId, role: null);
        return await scope.ServiceProvider.GetRequiredService<FleetShowcaseService>().GetCanonicalHostAsync();
    }

    [Fact]
    public async Task Yalniz_subdomain_aktifken_subdomain_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = await SeedTenantAsync();
        var sub = $"sub-{Guid.NewGuid():N}.rentpro.com";
        await AddDomainAsync(tenantId, sub, TenantDomainKind.Subdomain, TenantDomainStatus.Active);

        Assert.Equal(sub, await CanonicalAsync(host, tenantId));
    }

    [Fact]
    public async Task Aktif_ozel_domain_subdomaine_TERCIH_EDILIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = await SeedTenantAsync();
        var sub = $"sub-{Guid.NewGuid():N}.rentpro.com";
        var ozel = $"marka-{Guid.NewGuid():N}.com";
        await AddDomainAsync(tenantId, sub, TenantDomainKind.Subdomain, TenantDomainStatus.Active);
        await AddDomainAsync(tenantId, ozel, TenantDomainKind.Custom, TenantDomainStatus.Active,
            DateTimeOffset.UtcNow);

        Assert.Equal(ozel, await CanonicalAsync(host, tenantId)); // marka adresi kazanır
    }

    [Fact]
    public async Task Iki_aktif_ozel_domainde_EN_ESKI_dogrulanan_kazanir_deterministik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = await SeedTenantAsync();
        var eski = $"eski-{Guid.NewGuid():N}.com";
        var yeni = $"yeni-{Guid.NewGuid():N}.com";

        // Ekleme sırası BİLEREK ters (yeni önce) — seçim ekleme sırasına DEĞİL VerifiedAtUtc'ye bağlı olmalı.
        await AddDomainAsync(tenantId, yeni, TenantDomainKind.Custom, TenantDomainStatus.Active,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        await AddDomainAsync(tenantId, eski, TenantDomainKind.Custom, TenantDomainStatus.Active,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(eski, await CanonicalAsync(host, tenantId));
    }

    [Fact]
    public async Task Bekleyen_veya_basarisiz_ozel_domain_kanonik_SAYILMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = await SeedTenantAsync();
        var sub = $"sub-{Guid.NewGuid():N}.rentpro.com";
        await AddDomainAsync(tenantId, sub, TenantDomainKind.Subdomain, TenantDomainStatus.Active);
        await AddDomainAsync(tenantId, $"bekleyen-{Guid.NewGuid():N}.com", TenantDomainKind.Custom,
            TenantDomainStatus.PendingVerification);
        await AddDomainAsync(tenantId, $"basarisiz-{Guid.NewGuid():N}.com", TenantDomainKind.Custom,
            TenantDomainStatus.Failed);

        Assert.Equal(sub, await CanonicalAsync(host, tenantId)); // yalnız Active sayılır
    }

    [Fact]
    public async Task Hic_host_yoksa_null_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        Assert.Null(await CanonicalAsync(host, await SeedTenantAsync()));
    }

    [Fact]
    public async Task Kanonik_host_tenant_izoledir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = await SeedTenantAsync();
        var t2 = await SeedTenantAsync();
        await AddDomainAsync(t1, $"t1-{Guid.NewGuid():N}.rentpro.com", TenantDomainKind.Subdomain, TenantDomainStatus.Active);

        Assert.Null(await CanonicalAsync(host, t2)); // T1'in host'u T2'ye SIZMAZ
    }
}
