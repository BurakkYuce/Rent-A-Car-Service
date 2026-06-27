using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class VehicleCrudTests(PostgresFixture fx)
{
    [Fact]
    public async Task Empty_plaka_is_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new VehicleInput { Plaka = "   " }));
    }

    [Fact]
    public async Task Create_read_update_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await svc.CreateAsync(new VehicleInput
        {
            Plaka = "34 crud 01", Marka = "Renault", Durum = VehicleStatus.Musait, Km = 500, Yakit = FuelType.Dizel
        });

        var created = await svc.GetAsync(id);
        Assert.NotNull(created);
        Assert.Equal("34CRUD01", created!.Plaka); // normalize: büyük harf + boşluksuz
        Assert.Equal("Renault", created.Marka);

        var ok = await svc.UpdateAsync(id, new VehicleInput
        {
            Plaka = "34CRUD01", Marka = "Renault Clio", Durum = VehicleStatus.Kirada, Km = 1200, Yakit = FuelType.Dizel
        });
        Assert.True(ok);

        var updated = await svc.GetAsync(id);
        Assert.Equal("Renault Clio", updated!.Marka);
        Assert.Equal(VehicleStatus.Kirada, updated.Durum);
        Assert.Equal(1200, updated.Km);
    }

    [Fact]
    public async Task Duplicate_plaka_same_tenant_is_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        await svc.CreateAsync(new VehicleInput { Plaka = "34DUP01" });
        await Assert.ThrowsAsync<DuplicatePlakaException>(
            () => svc.CreateAsync(new VehicleInput { Plaka = "34DUP01" }));
    }

    [Fact]
    public async Task Same_plaka_different_tenants_is_allowed()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        const string plaka = "34SHARED9";

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = plaka });

        using (var s2 = host.ScopeFor(t2))
        {
            var id = await s2.ServiceProvider.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = plaka });
            Assert.NotEqual(Guid.Empty, id);
        }
    }

    [Fact]
    public async Task Delete_removes_vehicle()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await svc.CreateAsync(new VehicleInput { Plaka = "34DEL01" });
        Assert.True(await svc.DeleteAsync(id));
        Assert.Null(await svc.GetAsync(id));
    }
}
