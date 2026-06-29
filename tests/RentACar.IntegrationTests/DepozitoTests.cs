using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Periods;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap I3 — depozito al/iade/mahsup (PARA). BAĞIMSIZ ORACLE: al 1000 → depozito 1000; mahsup 400 →
/// depozito 600 / cari −400; iade 600 → depozito 0. İade tutulanı aşamaz; dönem-kilidi; idempotency.
/// </summary>
[Collection("postgres")]
public sealed class DepozitoTests(PostgresFixture fx)
{
    private static async Task<Guid> Cari(IServiceProvider sp)
        => await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Depozito Cari" });

    [Fact]
    public async Task Al_mahsup_iade_yasam_dongusu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        var dep = sp.GetRequiredService<DepozitoService>();
        var cash = sp.GetRequiredService<CashService>();

        await dep.AlAsync(cari, 1000m, LedgerAccountType.Kasa);
        Assert.Equal(1000m, await dep.GetBakiyeAsync(cari));   // tutulan depozito
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari)); // al cari'ye dokunmaz

        await dep.MahsupAsync(cari, 400m);
        Assert.Equal(600m, await dep.GetBakiyeAsync(cari));     // 1000 − 400
        Assert.Equal(-400m, await cash.GetCariBalanceAsync(cari)); // cari alacaklandı (borç azaldı)

        await dep.IadeAsync(cari, 600m, LedgerAccountType.Kasa);
        Assert.Equal(0m, await dep.GetBakiyeAsync(cari));       // tüm depozito iade
    }

    [Fact]
    public async Task Iade_tutulani_asamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        var dep = sp.GetRequiredService<DepozitoService>();
        await dep.AlAsync(cari, 500m, LedgerAccountType.Kasa);
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => dep.IadeAsync(cari, 600m, LedgerAccountType.Kasa)); // tutulan 500
    }

    [Fact]
    public async Task Donem_kilidi_engeller()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        await sp.GetRequiredService<DonemKilidiService>().LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<DepozitoService>().AlAsync(cari, 100m, LedgerAccountType.Kasa));
    }

    [Fact]
    public async Task Idempotency_anahtar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        var dep = sp.GetRequiredService<DepozitoService>();
        var key = Guid.NewGuid();
        await dep.AlAsync(cari, 1000m, LedgerAccountType.Kasa, islemAnahtari: key);
        await dep.AlAsync(cari, 1000m, LedgerAccountType.Kasa, islemAnahtari: key); // çift-submit
        Assert.Equal(1000m, await dep.GetBakiyeAsync(cari)); // çiftlenmedi
    }
}
