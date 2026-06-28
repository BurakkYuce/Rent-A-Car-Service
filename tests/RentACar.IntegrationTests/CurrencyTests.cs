using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Currencies;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Döviz master — bağımsız oracle. CRUD + kod normalize/3-harf doğrulama/benzersizlik +
/// aktif filtre + yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class CurrencyTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CurrencyService>();

        var id = await svc.CreateAsync(new CurrencyInput { Kod = "usd", Ad = "ABD Doları", Sembol = "$" });
        var got = await svc.GetAsync(id);
        Assert.Equal("USD", got!.Kod);
        Assert.Equal("ABD Doları", got.Ad);
        Assert.Equal("$", got.Sembol);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Kod_must_be_three_letters()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CurrencyService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CurrencyInput { Kod = "US", Ad = "x" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CurrencyInput { Kod = "USDD", Ad = "x" }));
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CurrencyService>();

        await svc.CreateAsync(new CurrencyInput { Kod = "EUR", Ad = "Euro" });
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CurrencyInput { Kod = "eur", Ad = "Başka" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CurrencyService>();

        var a = await svc.CreateAsync(new CurrencyInput { Kod = "TRY", Ad = "Türk Lirası" });
        await svc.CreateAsync(new CurrencyInput { Kod = "GBP", Ad = "Sterlin" });
        await svc.UpdateAsync(a, new CurrencyInput { Kod = "TRY", Ad = "Türk Lirası", Aktif = false });

        var active = await svc.ListActiveAsync();
        Assert.Single(active);
        Assert.Equal("GBP", active[0].Kod);
        Assert.Equal(2, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<CurrencyService>();
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CurrencyInput { Kod = "XXX", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task Currencies_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<CurrencyService>()
                .CreateAsync(new CurrencyInput { Kod = "USD", Ad = "Dolar" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<CurrencyService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new CurrencyInput { Kod = "USD", Ad = "Dolar T2" });
        Assert.Single(await svc2.ListAsync());
    }
}
