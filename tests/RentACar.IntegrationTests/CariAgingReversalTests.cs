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

/// <summary>
/// #367 adversarial incelemesinden kalıcılaştırılan durumlar: FIFO yaşlandırmada ters kayıt ve fatura iadesi YENİ bir
/// borç/alacak gibi sayılmamalı — asıl kaydıyla eşleşip birlikte düşmeli. Beklenen değerler elle kurulan senaryodan.
/// </summary>
[Collection("postgres")]
public sealed class CariAgingReversalTests(PostgresFixture fx)
{
    private static async Task<Guid> Vehicle(IServiceScope s, string plate)
    {
        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var v = new Vehicle { Plaka = plate, Durum = VehicleStatus.Musait };
        db.Vehicles.Add(v); await db.SaveChangesAsync(); return v.Id;
    }

    private static async Task<Guid> Customer(IServiceScope s, string name)
    {
        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var c = new Customer { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Yaslandirma" };
        db.Customers.Add(c); await db.SaveChangesAsync(); return c.Id;
    }

    /// <summary>
    /// 100 gün önceki 1.200 borç bugün tahsil edildi, tahsilat sonra TERS alındı (karşılıksız çek). Tahsilat hiç olmamış
    /// sayılır → borç 100 günlük, 90+ kovasında 1.200. (Eşleştirme yokken ters kayıt bugünkü yeni borç sayılıp 0-30'a düşüyordu.)
    /// </summary>
    [Fact]
    public async Task Reversed_collection_restores_original_debt_age()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var now = TestZaman.Now();

        var a = await Customer(scope, "Ters");
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = await Vehicle(scope, "34AV01"), AliciCariId = a, SatisNet = 1000m, KdvOrani = 0.20m, Tarih = now.AddDays(-100) });
        var tx = await cash.CollectAsync(new CashInput { CariId = a, Tutar = 1200m });
        await cash.ReverseAsync(tx);

        var row = Assert.Single(await reports.GetAgingAsync(now.AddMinutes(5)));
        Assert.Equal(1200m, row.Toplam);
        Assert.Equal(1200m, row.B90Plus);
        Assert.Equal(0m, row.B0_30);
    }

    /// <summary>
    /// 100 gün önce 1.200, 5 gün önce 600 fatura; YENİ fatura iade edildi. İade yalnız kendi faturasını kapatır →
    /// 90+ = 1.200, 0-30 = 0. (Eşleştirme yokken FIFO iadeyi eski borçtan düşüp 90+ = 600, 0-30 = 600 veriyordu.)
    /// </summary>
    [Fact]
    public async Task Invoice_refund_closes_its_own_invoice_not_the_oldest_debt()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var now = TestZaman.Now();

        var a = await Customer(scope, "Iade");
        await invoices.CreateManualAsync(new ManualInvoiceInput { CariId = a, NetTutar = 1000m, KdvOrani = 0.20m, Tarih = now.AddDays(-100) });
        var recent = await invoices.CreateManualAsync(new ManualInvoiceInput { CariId = a, NetTutar = 500m, KdvOrani = 0.20m, Tarih = now.AddDays(-5) });
        await invoices.CreateRefundAsync(recent);

        var row = Assert.Single(await reports.GetAgingAsync(now.AddMinutes(5)));
        Assert.Equal(1200m, row.B90Plus);
        Assert.Equal(0m, row.B0_30);
        Assert.Equal(1200m, row.Toplam);
        Assert.Equal(1200m, (await reports.GetAccountBalancesAsync()).Single(x => x.CariId == a).Bakiye);
    }

    /// <summary>
    /// Dövizli: USD borç 100 @ 30 (base 3.000, 40 gün), TL borç 2.000 (10 gün), 100 USD tahsilat @ 35 (base 3.500).
    /// FIFO base üzerinden: 40 günlük 3.000 kapanır, 10 günlükten 500 düşer → 0-30 = 1.500 = base bakiye.
    /// </summary>
    [Fact]
    public async Task Fx_debt_and_collection_buckets_in_base_and_match_balance()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var now = TestZaman.Now();

        var a = await Customer(scope, "Doviz");
        await sales.CreateAsync(new VehicleSaleInput
        {
            VehicleId = await Vehicle(scope, "34AV02"), AliciCariId = a, SatisNet = 100m, KdvOrani = 0m, Tarih = now.AddDays(-40),
            Doviz = "USD", Kur = 30m
        });
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = await Vehicle(scope, "34AV03"), AliciCariId = a, SatisNet = 2000m, KdvOrani = 0m, Tarih = now.AddDays(-10) });
        await cash.CollectAsync(new CashInput { CariId = a, Tutar = 100m, Doviz = "USD", Kur = 35m });

        var row = Assert.Single(await reports.GetAgingAsync(now.AddMinutes(5)));
        Assert.Equal(1500m, row.B0_30);
        Assert.Equal(0m, row.B31_60);
        Assert.Equal(1500m, row.Toplam);
        Assert.Equal(1500m, (await reports.GetAccountBalancesAsync()).Single(x => x.CariId == a).Bakiye);
    }
}
