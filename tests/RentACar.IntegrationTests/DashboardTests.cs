using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Dashboard;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap D3 — Dashboard. BAĞIMSIZ ORACLE: panel kartları mevcut raporlardan doğru derlenir
/// (aktif kira, bugün çıkış, kasa bakiye, bugün tahsilat, açık bakiye); boş tenant sıfır.
/// </summary>
[Collection("postgres")]
public sealed class DashboardTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Gun = new(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Composes_metrics_from_reports()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var vId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 DSH 01" });
        var cId = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Panel Müşteri" });
        await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cId, VehicleId = vId, BasTar = Gun, BitTar = Gun.AddDays(4), GunlukUcret = 100m });
        await sp.GetRequiredService<CashService>().CollectAsync(new CashInput
        { CariId = cId, Tutar = 500m, Kur = 1m, Doviz = "TRY", Hesap = LedgerAccountType.Kasa, Tarih = Gun });

        var d = await sp.GetRequiredService<DashboardService>().GetAsync(Gun);

        Assert.Equal(1, d.ToplamArac);
        Assert.True(d.AktifKira >= 1);                 // oluşturulan kira aktif
        Assert.Equal(1, d.BugunCikis);                 // kira başlangıcı = Gun
        Assert.Equal(500m, d.KasaBakiye);              // tahsilat 500 → kasa +500
        Assert.Equal(1, d.BugunTahsilatAdet);
        Assert.Equal(500m, d.BugunTahsilatTutar);
        Assert.Equal(-500m, d.AcikBakiye);             // fatura 0 − tahsilat 500 = -500 (elle oracle)
    }

    [Fact]
    public async Task Empty_tenant_is_zero()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var d = await scope.ServiceProvider.GetRequiredService<DashboardService>().GetAsync(Gun);

        Assert.Equal(0, d.ToplamArac);
        Assert.Equal(0, d.AktifKira);
        Assert.Equal(0m, d.KasaBakiye);
        Assert.Equal(0m, d.BankaBakiye);
        Assert.Equal(0, d.BugunCikis);
    }
}
