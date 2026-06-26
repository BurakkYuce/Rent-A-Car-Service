using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Tenant izolasyonu: racar_app (KISITLI, NOBYPASSRLS) rolüyle bağlı — bu kritik;
/// owner/superuser ile bağlansaydık RLS bypass olur ve test bir şey kanıtlamazdı.
/// Hem EF global query filter hem de RLS yolu doğrulanır.
/// </summary>
[Collection("postgres")]
public sealed class TenantIsolationTests(PostgresFixture fx)
{
    [Fact]
    public async Task Tenant_data_isolated_via_ef_filter_and_rls()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        await InsertVehicleAsync(host, t1, "34T1AAA");
        await InsertVehicleAsync(host, t2, "34T2BBB");

        using var scope = host.ScopeFor(t2);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        // (a) EF global query filter yolu: yalnız T2 görünür.
        var viaLinq = await db.Vehicles.Select(v => v.Plaka).ToListAsync();
        Assert.Equal(["34T2BBB"], viaLinq);

        // (b) RLS yolu: EF filter'ı atla (raw SQL + IgnoreQueryFilters) — T1 YİNE görünmez.
        var viaRaw = await db.Vehicles
            .FromSqlRaw("SELECT * FROM \"Vehicles\"")
            .IgnoreQueryFilters()
            .Select(v => v.Plaka)
            .ToListAsync();
        Assert.Equal(["34T2BBB"], viaRaw);

        // (c) Tamamen ham sayım da RLS'e tabidir.
        var rawCount = await db.Database
            .SqlQueryRaw<long>("SELECT count(*)::bigint AS \"Value\" FROM \"Vehicles\"")
            .SingleAsync();
        Assert.Equal(1, rawCount);
    }

    [Fact]
    public async Task Unset_tenant_sees_no_rows_default_deny()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        await InsertVehicleAsync(host, t1, "34DENY01");

        // Tenant yok → GUC '' → policy NULLIF(...,'')::uuid = NULL → hiçbir satır.
        using var scope = host.ScopeFor(tenantId: null);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var rawCount = await db.Database
            .SqlQueryRaw<long>("SELECT count(*)::bigint AS \"Value\" FROM \"Vehicles\"")
            .SingleAsync();
        Assert.Equal(0, rawCount);
    }

    [Fact]
    public async Task Cannot_write_row_for_other_tenant()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        // GUC = T1 iken TenantId = T2 olan satır INSERT → RLS WITH CHECK reddeder.
        using var scope = host.ScopeFor(t1);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.Vehicles.Add(new Vehicle { TenantId = t2, Plaka = "34HACK01" });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static async Task InsertVehicleAsync(TestHost host, Guid tenant, string plaka)
    {
        using var scope = host.ScopeFor(tenant);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.Vehicles.Add(new Vehicle { Plaka = plaka }); // TenantId interceptor tarafından damgalanır
        await db.SaveChangesAsync();
    }
}
