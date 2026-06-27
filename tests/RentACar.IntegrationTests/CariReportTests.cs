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
    private static async Task<Guid> SeedVehicleAsync(IServiceScope scope, string plaka)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var v = new Vehicle { Plaka = plaka, Durum = VehicleStatus.Musait };
        db.Vehicles.Add(v);
        await db.SaveChangesAsync();
        return v.Id;
    }

    private static async Task<Guid> SeedCustomerAsync(IServiceScope scope, string ad, string soyad)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var c = new Customer { Tip = CariType.Bireysel, Ad = ad, Soyad = soyad };
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
        var veli = await SeedCustomerAsync(scope, "Veli", "Han");
        var vid = await SeedVehicleAsync(scope, "34CB01");

        // Ali: satış net 5000 @0.20 → borç 6000; tahsilat 2000 → bakiye 4000.
        await sales.CreateAsync(new VehicleSaleInput { VehicleId = vid, AliciCariId = ali, SatisNet = 5000m, KdvOrani = 0.20m });
        await cash.CollectAsync(new CashInput { CariId = ali, Tutar = 2000m });
        // Veli: yalnız tahsilat 1000 → bakiye −1000 (alacaklı).
        await cash.CollectAsync(new CashInput { CariId = veli, Tutar = 1000m });

        var balances = await reports.GetCariBalancesAsync();
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

        var cari = await SeedCustomerAsync(scope, "Yaş", "Test");
        var v1 = await SeedVehicleAsync(scope, "34AG01");
        var v2 = await SeedVehicleAsync(scope, "34AG02");
        var asOf = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        // 45 gün önce: net 1000 → borç 1200 → 31-60 kovası.
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = v1, AliciCariId = cari, SatisNet = 1000m, KdvOrani = 0.20m, Tarih = asOf.AddDays(-45) });
        // 10 gün önce: net 2000 → borç 2400 → 0-30 kovası.
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = v2, AliciCariId = cari, SatisNet = 2000m, KdvOrani = 0.20m, Tarih = asOf.AddDays(-10) });

        var aging = Assert.Single(await reports.GetAgingAsync(asOf));
        Assert.Equal(2400m, aging.B0_30);
        Assert.Equal(1200m, aging.B31_60);
        Assert.Equal(0m, aging.B61_90);
        Assert.Equal(0m, aging.B90Plus);
        Assert.Equal(3600m, aging.Toplam);
    }

    [Fact]
    public async Task Cari_reports_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            var cari = await SeedCustomerAsync(s1, "T1", "Cari");
            await s1.ServiceProvider.GetRequiredService<CashService>()
                .CollectAsync(new CashInput { CariId = cari, Tutar = 500m });
        }

        using var s2 = host.ScopeFor(t2);
        var reports = s2.ServiceProvider.GetRequiredService<ReportService>();
        Assert.Empty(await reports.GetCariBalancesAsync());
        Assert.Empty(await reports.GetAgingAsync(DateTimeOffset.UtcNow));
    }
}
