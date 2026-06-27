using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kasa/Banka tam modülü — bağımsız oracle. Beklenen değerler işlem GİRDİLERİNDEN elle türetilir
/// (servis kodundan değil). Semantik:
///   Tahsilat: Borç Hesap / Alacak Cari → cari bakiye ↓, hesap ↑
///   Ödeme:    Borç Cari / Alacak Hesap → cari bakiye ↑, hesap ↓
///   Virman:   Borç Hedef / Alacak Kaynak (dengeli, cari yok)
///   Ters:     yönler çevrilir → orijinali sıfırlar
/// </summary>
[Collection("postgres")]
public sealed class KasaBankaTests(PostgresFixture fx)
{
    private static async Task<Guid> SeedCariAsync(IServiceScope scope, string ad)
    {
        var customers = scope.ServiceProvider.GetRequiredService<CustomerService>();
        return await customers.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = ad, Soyad = "Test" });
    }

    [Fact]
    public async Task Odeme_increases_cari_balance_and_reduces_kasa()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await SeedCariAsync(scope, "Odeme");

        // Ödeme 1000: Borç Cari (+1000) / Alacak Kasa (−1000).
        await cash.PayAsync(new CashInput { CariId = cari, Tutar = 1000m, Hesap = LedgerAccountType.Kasa });

        Assert.Equal(1000m, await cash.GetCariBalanceAsync(cari));
        var s = await reports.GetKasaBankaSummaryAsync();
        Assert.Equal(0m, s.KasaGiris);
        Assert.Equal(1000m, s.KasaCikis);
        Assert.Equal(-1000m, s.KasaBakiye);
    }

    [Fact]
    public async Task Odeme_via_banka_touches_banka_only()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await SeedCariAsync(scope, "BankaOdeme");

        await cash.PayAsync(new CashInput { CariId = cari, Tutar = 800m, Hesap = LedgerAccountType.Banka });

        var s = await reports.GetKasaBankaSummaryAsync();
        Assert.Equal(0m, s.KasaCikis);
        Assert.Equal(0m, s.KasaBakiye);
        Assert.Equal(800m, s.BankaCikis);
        Assert.Equal(-800m, s.BankaBakiye);
    }

    [Fact]
    public async Task Virman_kasa_to_banka_is_balanced()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await SeedCariAsync(scope, "Virman");

        // Önce kasaya 2000 giriş (tahsilat), sonra 500 kasa→banka.
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 2000m, Hesap = LedgerAccountType.Kasa });
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 500m);

        var s = await reports.GetKasaBankaSummaryAsync();
        Assert.Equal(2000m, s.KasaGiris);
        Assert.Equal(500m, s.KasaCikis);
        Assert.Equal(1500m, s.KasaBakiye);
        Assert.Equal(500m, s.BankaGiris);
        Assert.Equal(0m, s.BankaCikis);
        Assert.Equal(500m, s.BankaBakiye);
        // Toplam kasa+banka korunur (virman değer yaratmaz/yok etmez).
        Assert.Equal(2000m, s.KasaBakiye + s.BankaBakiye);
        // Virman cari'ye dokunmaz: bakiye yalnız tahsilattan (−2000).
        Assert.Equal(-2000m, await cash.GetCariBalanceAsync(cari));
    }

    [Fact]
    public async Task Reverse_odeme_zeroes_cari_and_kasa()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await SeedCariAsync(scope, "TersOdeme");

        var id = await cash.PayAsync(new CashInput { CariId = cari, Tutar = 1000m, Hesap = LedgerAccountType.Kasa });
        Assert.Equal(1000m, await cash.GetCariBalanceAsync(cari));

        await cash.ReverseAsync(id);

        // Ters kayıt orijinali sıfırlar: cari 0, kasa 0 (giriş=çıkış=1000).
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));
        var s = await reports.GetKasaBankaSummaryAsync();
        Assert.Equal(1000m, s.KasaGiris);
        Assert.Equal(1000m, s.KasaCikis);
        Assert.Equal(0m, s.KasaBakiye);
    }

    [Fact]
    public async Task Transfer_same_account_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Kasa, 100m));
    }

    [Fact]
    public async Task Transfer_non_cash_account_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Gelir, 100m));
    }

    [Fact]
    public async Task Odeme_non_cash_account_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await SeedCariAsync(scope, "GecersizHesap");
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.PayAsync(new CashInput { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Gelir }));
    }

    [Fact]
    public async Task NonFinance_user_cannot_pay()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid cari;
        using (var admin = host.ScopeFor(tenant))
            cari = await SeedCariAsync(admin, "YetkiYok");

        using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.PayAsync(new CashInput { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Kasa }));
    }
}
