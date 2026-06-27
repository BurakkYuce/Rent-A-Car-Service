using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Locations;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Ofis/Lokasyon master — bağımsız oracle. CRUD + kod normalize/benzersizlik + aktif filtre
/// (dropdown kaynağı) + yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class LocationTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        var id = await svc.CreateAsync(new LocationInput { Kod = "ist-hvl", Ad = "İstanbul Havalimanı", Sube = "Merkez" });
        var got = await svc.GetAsync(id);
        Assert.Equal("IST-HVL", got!.Kod);
        Assert.Equal("İstanbul Havalimanı", got.Ad);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        await svc.CreateAsync(new LocationInput { Kod = "MERKEZ", Ad = "Merkez Ofis" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new LocationInput { Kod = "merkez", Ad = "Başka" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        var a = await svc.CreateAsync(new LocationInput { Kod = "A", Ad = "A Ofis" });
        await svc.CreateAsync(new LocationInput { Kod = "B", Ad = "B Ofis" });
        await svc.UpdateAsync(a, new LocationInput { Kod = "A", Ad = "A Ofis", Aktif = false });

        var active = await svc.ListActiveAsync();
        Assert.Single(active);
        Assert.Equal("B", active[0].Kod);
        Assert.Equal(2, (await svc.ListAsync()).Count); // pasif korunur
    }

    [Fact]
    public async Task Validation_requires_kod_and_ad()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new LocationInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new LocationInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task Delete_removes_location()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        var id = await svc.CreateAsync(new LocationInput { Kod = "DEL", Ad = "Silinecek" });
        Assert.True(await svc.DeleteAsync(id));
        Assert.Null(await svc.GetAsync(id));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new LocationInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task Locations_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<LocationService>()
                .CreateAsync(new LocationInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<LocationService>();
        Assert.Empty(await svc2.ListAsync());
        // Aynı kod farklı tenant'ta serbest.
        await svc2.CreateAsync(new LocationInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
