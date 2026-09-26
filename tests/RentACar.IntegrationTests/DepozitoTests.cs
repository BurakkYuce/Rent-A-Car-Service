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
        => await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Depozito Cari" });

    [Fact]
    public async Task Al_mahsup_iade_yasam_dongusu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        var dep = sp.GetRequiredService<DepositService>();
        var cash = sp.GetRequiredService<CashService>();

        await dep.GetAsync(cari, 1000m, LedgerAccountType.Kasa);
        Assert.Equal(1000m, await dep.GetBalanceAsync(cari));   // tutulan depozito
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(cari)); // al cari'ye dokunmaz

        await dep.OffsetAsync(cari, 400m);
        Assert.Equal(600m, await dep.GetBalanceAsync(cari));     // 1000 − 400
        Assert.Equal(-400m, await cash.GetAccountBalanceAsync(cari)); // cari alacaklandı (borç azaldı)

        await dep.RefundAsync(cari, 600m, LedgerAccountType.Kasa);
        Assert.Equal(0m, await dep.GetBalanceAsync(cari));       // tüm depozito iade
    }

    [Fact]
    public async Task Iade_tutulani_asamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        var dep = sp.GetRequiredService<DepositService>();
        await dep.GetAsync(cari, 500m, LedgerAccountType.Kasa);
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => dep.RefundAsync(cari, 600m, LedgerAccountType.Kasa)); // tutulan 500
    }

    [Fact]
    public async Task Donem_kilidi_engeller()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        await sp.GetRequiredService<PeriodLockService>().LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<DepositService>().GetAsync(cari, 100m, LedgerAccountType.Kasa));
    }

    // Denetim O10a — negatif/sıfır tutar ve kur guard'ları: hepsi ValidationException,
    // depozito bakiyesi 0 KALIR (hiçbir kayıt sızmadı).
    [Fact]
    public async Task Negatif_veya_sifir_tutar_ve_kur_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        var dep = sp.GetRequiredService<DepositService>();

        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => dep.GetAsync(cari, 0m, LedgerAccountType.Kasa));            // tutar = 0
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => dep.GetAsync(cari, -100m, LedgerAccountType.Kasa));         // tutar < 0
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => dep.GetAsync(cari, 100m, LedgerAccountType.Kasa, "USD", 0m));   // kur = 0
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => dep.GetAsync(cari, 100m, LedgerAccountType.Kasa, "USD", -35m)); // kur < 0

        Assert.Equal(0m, await dep.GetBalanceAsync(cari)); // hiçbir şey yazılmadı
    }

    [Fact]
    public async Task Idempotency_anahtar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        var dep = sp.GetRequiredService<DepositService>();
        var key = Guid.NewGuid();
        await dep.GetAsync(cari, 1000m, LedgerAccountType.Kasa, operationKey: key);
        await dep.GetAsync(cari, 1000m, LedgerAccountType.Kasa, operationKey: key); // çift-submit
        Assert.Equal(1000m, await dep.GetBalanceAsync(cari)); // çiftlenmedi
    }
}
