using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.VehicleGroups;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Araç grubu master (basit sözlük) — bağımsız oracle. CRUD + kod normalize/benzersizlik +
/// aktif filtre (dropdown kaynağı) + yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class VehicleGroupTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        var id = await svc.CreateAsync(new VehicleGroupInput { Kod = "eko", Ad = "Ekonomik", Aciklama = "Düşük segment" });
        var got = await svc.GetAsync(id);
        Assert.Equal("EKO", got!.Kod);
        Assert.Equal("Ekonomik", got.Ad);
        Assert.Equal("Düşük segment", got.Aciklama);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        await svc.CreateAsync(new VehicleGroupInput { Kod = "SUV", Ad = "SUV" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new VehicleGroupInput { Kod = "suv", Ad = "Başka" }));
    }

    [Fact]
    public async Task Blank_aciklama_stored_as_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        var id = await svc.CreateAsync(new VehicleGroupInput { Kod = "X", Ad = "X Grup", Aciklama = "   " });
        Assert.Null((await svc.GetAsync(id))!.Aciklama);
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        var a = await svc.CreateAsync(new VehicleGroupInput { Kod = "A", Ad = "A Grup" });
        await svc.CreateAsync(new VehicleGroupInput { Kod = "B", Ad = "B Grup" });
        await svc.UpdateAsync(a, new VehicleGroupInput { Kod = "A", Ad = "A Grup", Aktif = false });

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
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new VehicleGroupInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new VehicleGroupInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task Delete_removes_group()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        var id = await svc.CreateAsync(new VehicleGroupInput { Kod = "DEL", Ad = "Silinecek" });
        Assert.True(await svc.DeleteAsync(id));
        Assert.Null(await svc.GetAsync(id));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new VehicleGroupInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task VehicleGroups_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<VehicleGroupService>()
                .CreateAsync(new VehicleGroupInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<VehicleGroupService>();
        Assert.Empty(await svc2.ListAsync());
        // Aynı kod farklı tenant'ta serbest.
        await svc2.CreateAsync(new VehicleGroupInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
