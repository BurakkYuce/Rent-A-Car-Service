using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class CariReportTests(PostgresFixture fx)
{
    private static async Task<Guid> SeedVehicleAsync(IServiceScope scope, string plate)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var v = new Vehicle { Plaka = plate, Durum = VehicleStatus.Musait };
        db.Vehicles.Add(v);
        await db.SaveChangesAsync();
        return v.Id;
    }

    private static async Task<Guid> SeedCustomerAsync(IServiceScope scope, string name, string soyad)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var c = new Customer { Tip = CustomerType.Bireysel, Ad = name, Soyad = soyad };
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    [Fact]
    public async Task Cari_balances_net_debit_minus_credit_and_resolve_names()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        var ali = await SeedCustomerAsync(scope, "Ali", "Veli");
        var guardian = await SeedCustomerAsync(scope, "Veli", "Han");
        var vid = await SeedVehicleAsync(scope, "34CB01");

        // Ali: satış net 5000 @0.20 → borç 6000; tahsilat 2000 → bakiye 4000.
        await sales.CreateAsync(new VehicleSaleInput { VehicleId = vid, AliciCariId = ali, SatisNet = 5000m, KdvOrani = 0.20m });
        await cash.CollectAsync(new CashInput { CariId = ali, Tutar = 2000m });
        // Veli: yalnız tahsilat 1000 → bakiye −1000 (alacaklı).
        await cash.CollectAsync(new CashInput { CariId = guardian, Tutar = 1000m });

        var balances = await reports.GetAccountBalancesAsync();
        Assert.Equal(2, balances.Count);
        Assert.Equal(ali, balances[0].CariId);       // en yüksek bakiye önce
        Assert.Equal("Ali Veli", balances[0].Ad);    // DisplayName çözümlendi
        Assert.Equal(4000m, balances[0].Bakiye);
        Assert.Equal(-1000m, balances[1].Bakiye);
    }

    [Fact]
    public async Task Aging_buckets_gross_debit_by_age()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        var account = await SeedCustomerAsync(scope, "Yaş", "Test");
        var v1 = await SeedVehicleAsync(scope, "34AG01");
        var v2 = await SeedVehicleAsync(scope, "34AG02");
        var asOf = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        // 45 gün önce: net 1000 → borç 1200 → 31-60 kovası.
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = v1, AliciCariId = account, SatisNet = 1000m, KdvOrani = 0.20m, Tarih = asOf.AddDays(-45) });
        // 10 gün önce: net 2000 → borç 2400 → 0-30 kovası.
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = v2, AliciCariId = account, SatisNet = 2000m, KdvOrani = 0.20m, Tarih = asOf.AddDays(-10) });

        var aging = Assert.Single(await reports.GetAgingAsync(asOf));
        Assert.Equal(2400m, aging.B0_30);
        Assert.Equal(1200m, aging.B31_60);
        Assert.Equal(0m, aging.B61_90);
        Assert.Equal(0m, aging.B90Plus);
        Assert.Equal(3600m, aging.Toplam);
    }

    /// <summary>
    /// Kabul bulgusu d-rapor-cari-bakiye-03: tahsilat yaşlandırmadan düşmüyordu (bakiye 700, kova 1.400).
    /// Beklenen değerler elle kurulan senaryodan, FIFO ile kâğıt üstünde hesaplandı.
    /// </summary>
    [Fact]
    public async Task Aging_applies_collections_fifo_to_oldest_debt_and_buckets_sum_to_balance()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var now = TestZaman.Now();

        // A: 75 gün önce 1.200, 45 gün önce 600, 5 gün önce 2.400 borç; bugün 1.500 tahsilat.
        //    FIFO: 1.500 → önce 1.200 (75 gün) tamamen, sonra 600'ün 300'ü → 45 günlükten 300 kalır.
        //    Kovalar: 0-30 = 2.400, 31-60 = 300, 61-90 = 0, 90+ = 0 → toplam 2.700 = 4.200 − 1.500.
        var a = await SeedCustomerAsync(scope, "Fifo", "Borclu");
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = await SeedVehicleAsync(scope, "34FF01"), AliciCariId = a, SatisNet = 1000m, KdvOrani = 0.20m, Tarih = now.AddDays(-75) });
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = await SeedVehicleAsync(scope, "34FF02"), AliciCariId = a, SatisNet = 500m, KdvOrani = 0.20m, Tarih = now.AddDays(-45) });
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = await SeedVehicleAsync(scope, "34FF03"), AliciCariId = a, SatisNet = 2000m, KdvOrani = 0.20m, Tarih = now.AddDays(-5) });
        await cash.CollectAsync(new CashInput { CariId = a, Tutar = 1500m });

        // B: 1.200 borç, 1.200 tahsilat → bakiye 0 → listede YOK.
        var b = await SeedCustomerAsync(scope, "Fifo", "Kapali");
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = await SeedVehicleAsync(scope, "34FF04"), AliciCariId = b, SatisNet = 1000m, KdvOrani = 0.20m, Tarih = now.AddDays(-20) });
        await cash.CollectAsync(new CashInput { CariId = b, Tutar = 1200m });

        // C: yalnız 500 tahsilat (avans) → alacaklı → yaşlanacak alacak yok → listede YOK.
        var c = await SeedCustomerAsync(scope, "Fifo", "Avans");
        await cash.CollectAsync(new CashInput { CariId = c, Tutar = 500m });

        var aging = Assert.Single(await reports.GetAgingAsync(now.AddMinutes(5)));
        Assert.Equal(a, aging.CariId);
        Assert.Equal(2400m, aging.B0_30);
        Assert.Equal(300m, aging.B31_60);
        Assert.Equal(0m, aging.B61_90);
        Assert.Equal(0m, aging.B90Plus);
        Assert.Equal(2700m, aging.Toplam);

        // Kovaların toplamı cari bakiyesiyle aynı (bakiye raporu da elle hesaplanan 2.700'ü vermeli).
        var balance = (await reports.GetAccountBalancesAsync()).Single(x => x.CariId == a);
        Assert.Equal(2700m, balance.Bakiye);
    }

    [Fact]
    public async Task Cari_reports_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            var account = await SeedCustomerAsync(s1, "T1", "Cari");
            await s1.ServiceProvider.GetRequiredService<CashService>()
                .CollectAsync(new CashInput { CariId = account, Tutar = 500m });
        }

        using var s2 = host.ScopeFor(t2);
        var reports = s2.ServiceProvider.GetRequiredService<ReportService>();
        Assert.Empty(await reports.GetAccountBalancesAsync());
        Assert.Empty(await reports.GetAgingAsync(DateTimeOffset.UtcNow));
    }
}
