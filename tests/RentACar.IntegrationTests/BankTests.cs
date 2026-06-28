using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Banks;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Banka master — bağımsız oracle. CRUD + kod normalize/benzersizlik + aktif filtre +
/// yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class BankTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BankService>();

        var id = await svc.CreateAsync(new BankInput { Kod = "ziraat", Ad = "Ziraat Bankası" });
        var got = await svc.GetAsync(id);
        Assert.Equal("ZIRAAT", got!.Kod);
        Assert.Equal("Ziraat Bankası", got.Ad);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BankService>();

        await svc.CreateAsync(new BankInput { Kod = "GARANTI", Ad = "Garanti" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BankInput { Kod = "garanti", Ad = "Başka" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BankService>();

        var a = await svc.CreateAsync(new BankInput { Kod = "A", Ad = "A Banka" });
        await svc.CreateAsync(new BankInput { Kod = "B", Ad = "B Banka" });
        await svc.UpdateAsync(a, new BankInput { Kod = "A", Ad = "A Banka", Aktif = false });

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
        var svc = scope.ServiceProvider.GetRequiredService<BankService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new BankInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new BankInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<BankService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BankInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task Banks_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<BankService>()
                .CreateAsync(new BankInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<BankService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new BankInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
