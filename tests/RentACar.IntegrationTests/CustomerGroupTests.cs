using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.CustomerGroups;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Müşteri grubu master — bağımsız oracle. CRUD + kod normalize/benzersizlik + aktif filtre +
/// yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class CustomerGroupTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerGroupService>();

        var id = await svc.CreateAsync(new CustomerGroupInput { Kod = "kurumsal", Ad = "Kurumsal" });
        var got = await svc.GetAsync(id);
        Assert.Equal("KURUMSAL", got!.Kod);
        Assert.Equal("Kurumsal", got.Ad);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerGroupService>();

        await svc.CreateAsync(new CustomerGroupInput { Kod = "BIREYSEL", Ad = "Bireysel" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CustomerGroupInput { Kod = "bireysel", Ad = "Başka" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerGroupService>();

        var a = await svc.CreateAsync(new CustomerGroupInput { Kod = "A", Ad = "A Grup" });
        await svc.CreateAsync(new CustomerGroupInput { Kod = "B", Ad = "B Grup" });
        await svc.UpdateAsync(a, new CustomerGroupInput { Kod = "A", Ad = "A Grup", Aktif = false });

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
        var svc = scope.ServiceProvider.GetRequiredService<CustomerGroupService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CustomerGroupInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CustomerGroupInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerGroupService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CustomerGroupInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task Groups_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<CustomerGroupService>()
                .CreateAsync(new CustomerGroupInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<CustomerGroupService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new CustomerGroupInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
