using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Accessories;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Aksesuar master — bağımsız oracle. CRUD + kod normalize/benzersizlik + aktif filtre +
/// yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class AccessoryTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AccessoryService>();

        var id = await svc.CreateAsync(new AccessoryInput { Kod = "bebek", Ad = "Bebek Koltuğu", Aciklama = "0-4 yaş" });
        var got = await svc.GetAsync(id);
        Assert.Equal("BEBEK", got!.Kod);
        Assert.Equal("Bebek Koltuğu", got.Ad);
        Assert.Equal("0-4 yaş", got.Aciklama);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AccessoryService>();

        await svc.CreateAsync(new AccessoryInput { Kod = "KAR-LASTIK", Ad = "Kar Lastiği" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new AccessoryInput { Kod = "kar-lastik", Ad = "Başka" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AccessoryService>();

        var a = await svc.CreateAsync(new AccessoryInput { Kod = "A", Ad = "A Aks" });
        await svc.CreateAsync(new AccessoryInput { Kod = "B", Ad = "B Aks" });
        await svc.UpdateAsync(a, new AccessoryInput { Kod = "A", Ad = "A Aks", Aktif = false });

        var active = await svc.ListActiveAsync();
        Assert.Single(active);
        Assert.Equal("B", active[0].Kod);
        Assert.Equal(2, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task Validation_requires_kod_and_ad()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AccessoryService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new AccessoryInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new AccessoryInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<AccessoryService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new AccessoryInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task Accessories_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<AccessoryService>()
                .CreateAsync(new AccessoryInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<AccessoryService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new AccessoryInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
