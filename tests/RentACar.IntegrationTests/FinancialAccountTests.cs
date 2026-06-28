using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.FinancialAccounts;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kasa/Banka hesap master — bağımsız oracle. CRUD + kod/döviz normalize + benzersizlik +
/// döviz uzunluk doğrulama + aktif filtre + yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class FinancialAccountTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_doviz_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<FinancialAccountService>();

        var id = await svc.CreateAsync(new FinancialAccountInput { Kod = "kasa-1", Ad = "Merkez Kasa", Tur = "Kasa", Doviz = "try" });
        var got = await svc.GetAsync(id);
        Assert.Equal("KASA-1", got!.Kod);
        Assert.Equal("Merkez Kasa", got.Ad);
        Assert.Equal("Kasa", got.Tur);
        Assert.Equal("TRY", got.Doviz);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<FinancialAccountService>();

        await svc.CreateAsync(new FinancialAccountInput { Kod = "ZIRAAT", Ad = "Ziraat TL" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new FinancialAccountInput { Kod = "ziraat", Ad = "Başka" }));
    }

    [Fact]
    public async Task Invalid_doviz_length_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<FinancialAccountService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new FinancialAccountInput { Kod = "X", Ad = "Hatalı", Doviz = "TURK" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<FinancialAccountService>();

        var a = await svc.CreateAsync(new FinancialAccountInput { Kod = "A", Ad = "A Hesap" });
        await svc.CreateAsync(new FinancialAccountInput { Kod = "B", Ad = "B Hesap" });
        await svc.UpdateAsync(a, new FinancialAccountInput { Kod = "A", Ad = "A Hesap", Aktif = false });

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
        var svc = scope.ServiceProvider.GetRequiredService<FinancialAccountService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new FinancialAccountInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new FinancialAccountInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<FinancialAccountService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new FinancialAccountInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task Accounts_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<FinancialAccountService>()
                .CreateAsync(new FinancialAccountInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<FinancialAccountService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new FinancialAccountInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
