using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Tahsilat-Fatura mutabakatı — bağımsız oracle. Dönem Haziran. Fatura 1000 (İptal+dönem dışı
/// hariç), tahsilat 600 (ters+dönem dışı hariç) → fark 400. Beklenenler senaryodan.
/// </summary>
[Collection("postgres")]
public sealed class TahsilatFaturaTests(PostgresFixture fx)
{
    private static DateTimeOffset At(int gun) => new(2026, 6, gun, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reconciliation_excludes_cancelled_reversed_and_out_of_period()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            // Faturalar: 600 + 400 = 1000 (dönem içi, Kesildi). İptal 9999 ve dönem dışı 700 hariç.
            db.Invoices.Add(new Invoice { No = "FT-1", Durum = InvoiceStatus.Kesildi, CariId = Guid.NewGuid(), Tarih = At(5), NetTutar = 500m, KdvTutar = 100m, GenelToplam = 600m, Currency = "TRY", Kur = 1m });
            db.Invoices.Add(new Invoice { No = "FT-2", Durum = InvoiceStatus.Kesildi, CariId = Guid.NewGuid(), Tarih = At(8), NetTutar = 340m, KdvTutar = 60m, GenelToplam = 400m, Currency = "TRY", Kur = 1m });
            db.Invoices.Add(new Invoice { No = "FT-IPT", Durum = InvoiceStatus.Iptal, CariId = Guid.NewGuid(), Tarih = At(6), NetTutar = 9999m, KdvTutar = 0m, GenelToplam = 9999m, Currency = "TRY", Kur = 1m }); // hariç
            db.Invoices.Add(new Invoice { No = "FT-OUT", Durum = InvoiceStatus.Kesildi, CariId = Guid.NewGuid(), Tarih = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero), NetTutar = 700m, KdvTutar = 0m, GenelToplam = 700m, Currency = "TRY", Kur = 1m }); // hariç

            // Tahsilatlar: 250 + 350 = 600. Ters 9999 ve dönem dışı 500 hariç.
            db.CashTransactions.Add(new CashTransaction { No = "NT-1", Tip = CashTransactionType.Tahsilat, CariId = Guid.NewGuid(), Tarih = At(5), Amount = new Money(250m, "TRY", 1m) });
            db.CashTransactions.Add(new CashTransaction { No = "NT-2", Tip = CashTransactionType.Tahsilat, CariId = Guid.NewGuid(), Tarih = At(9), Amount = new Money(350m, "TRY", 1m) });
            db.CashTransactions.Add(new CashTransaction { No = "NT-TERS", Tip = CashTransactionType.Tahsilat, CariId = Guid.NewGuid(), Tarih = At(7), Amount = new Money(9999m, "TRY", 1m), TersKayitMi = true }); // hariç
            db.CashTransactions.Add(new CashTransaction { No = "NT-ODE", Tip = CashTransactionType.Odeme, CariId = Guid.NewGuid(), Tarih = At(7), Amount = new Money(123m, "TRY", 1m) }); // Tahsilat değil → hariç
            db.CashTransactions.Add(new CashTransaction { No = "NT-OUT", Tip = CashTransactionType.Tahsilat, CariId = Guid.NewGuid(), Tarih = new DateTimeOffset(2026, 5, 30, 10, 0, 0, TimeSpan.Zero), Amount = new Money(500m, "TRY", 1m) }); // hariç
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);
        var g = await svc.GetTahsilatFaturaAsync(from, to);

        Assert.Equal(2, g.FaturaAdet);
        Assert.Equal(1000m, g.FaturaToplam);
        Assert.Equal(2, g.TahsilatAdet);
        Assert.Equal(600m, g.TahsilatToplam);
        Assert.Equal(400m, g.Fark);
    }

    [Fact]
    public async Task Multi_currency_uses_base_amount()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            // Fatura 100 USD × Kur 40 = 4000 base. Tahsilat 50 USD × Rate 40 = 2000 base. Fark 2000.
            db.Invoices.Add(new Invoice { No = "FT-USD", Durum = InvoiceStatus.Kesildi, CariId = Guid.NewGuid(), Tarih = At(5), NetTutar = 100m, KdvTutar = 0m, GenelToplam = 100m, Currency = "USD", Kur = 40m });
            db.CashTransactions.Add(new CashTransaction { No = "NT-USD", Tip = CashTransactionType.Tahsilat, CariId = Guid.NewGuid(), Tarih = At(6), Amount = new Money(50m, "USD", 40m) });
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);
        var g = await svc.GetTahsilatFaturaAsync(from, to);

        Assert.Equal(4000m, g.FaturaToplam);   // 100 × 40
        Assert.Equal(2000m, g.TahsilatToplam); // 50 × 40
        Assert.Equal(2000m, g.Fark);
    }

    [Fact]
    public async Task Tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var factory = s1.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.Invoices.Add(new Invoice { No = "FT-T", Durum = InvoiceStatus.Kesildi, CariId = Guid.NewGuid(), Tarih = At(5), NetTutar = 100m, KdvTutar = 0m, GenelToplam = 100m, Currency = "TRY", Kur = 1m });
            await db.SaveChangesAsync();
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var g = await s2.ServiceProvider.GetRequiredService<ReportService>().GetTahsilatFaturaAsync();
        Assert.Equal(0, g.FaturaAdet);
        Assert.Equal(0m, g.FaturaToplam);
    }
}
