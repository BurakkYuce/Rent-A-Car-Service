using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-4.5: `ITenantContext.ThrowIfTenantMissing` — varsayılan false (mevcut default-deny davranışı,
/// bkz. `TenantIsolationTests.Unset_tenant_sees_no_rows_default_deny`, DEĞİŞMEZ); yalnız `true` işaretli
/// bağlamlarda (ör. PublicTenantContext) TenantId boşken gürültülü hata.
/// </summary>
[Collection("postgres")]
public sealed class TenantConnectionInterceptorTests(PostgresFixture fx)
{
    [Fact]
    public async Task ThrowIfTenantMissing_true_ve_TenantId_null_iken_sorgu_istisna_atar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenantId: null);
        var identity = scope.ServiceProvider.GetRequiredService<TestIdentity>();
        identity.ThrowIfTenantMissing = true;

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.Database.SqlQueryRaw<long>("SELECT count(*)::bigint AS \"Value\" FROM \"Vehicles\"").SingleAsync());
    }

    [Fact]
    public async Task ThrowIfTenantMissing_false_iken_TenantId_null_sessizce_bos_doner()
    {
        // Regresyon-kilidi: varsayılan (false) davranış TenantIsolationTests'teki default-deny ile AYNI kalmalı.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenantId: null); // ThrowIfTenantMissing set edilmedi → false
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var count = await db.Database.SqlQueryRaw<long>("SELECT count(*)::bigint AS \"Value\" FROM \"Vehicles\"").SingleAsync();
        Assert.Equal(0, count);
    }
}
