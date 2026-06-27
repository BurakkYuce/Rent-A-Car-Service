using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class ReportingTests(PostgresFixture fx)
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

    /// <summary>
    /// Bağımsız oracle: bilinen işlemleri (nakit gider + araç satış + tahsilat) servislerle ekle,
    /// raporun ELLE-HESAPLI toplamlara eşit olduğunu doğrula. Beklenen değerler rapor kodundan
    /// DEĞİL, işlem girdilerinden türetilir.
    /// </summary>
    private static async Task SeedKnownLedgerAsync(IServiceScope scope, Guid alici)
    {
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var vid = await SeedVehicleAsync(scope, "34RPR34");

        // Nakit gider: net 1000 @0.20 → Gider(D 1000) + Kdv(D 200) / Kasa(C 1200).
        await expenses.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 1000m, KdvOrani = 0.20m, OdemeYontemi = OdemeYontemi.Nakit });

        // Araç satış: net 5000 @0.20 → Cari(D 6000) / Gelir(C 5000) + Kdv(C 1000).
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = vid, AliciCariId = alici, SatisNet = 5000m, KdvOrani = 0.20m });

        // Tahsilat: 3000 → Kasa(D 3000) / Cari(C 3000).
        await cash.CollectAsync(new CashInput { CariId = alici, Tutar = 3000m });
    }

    [Fact]
    public async Task Kasa_banka_summary_matches_posted_transactions()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedKnownLedgerAsync(scope, Guid.NewGuid());
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        var s = await reports.GetKasaBankaSummaryAsync();
        Assert.Equal(3000m, s.KasaGiris);   // tahsilat
        Assert.Equal(1200m, s.KasaCikis);   // nakit gider brüt
        Assert.Equal(1800m, s.KasaBakiye);  // 3000 − 1200
        Assert.Equal(0m, s.BankaGiris);
        Assert.Equal(0m, s.BankaCikis);
        Assert.Equal(0m, s.BankaBakiye);
    }

    [Fact]
    public async Task Account_ledger_running_balance_ends_at_account_balance()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedKnownLedgerAsync(scope, Guid.NewGuid());
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        var lines = await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa);
        Assert.Equal(2, lines.Count);                 // gider çıkışı + tahsilat girişi
        Assert.Equal(1800m, lines[^1].YuruyenBakiye); // yürüyen bakiye = kasa bakiye (sıradan bağımsız net)
        // Σ Borç − Σ Alacak = bakiye.
        Assert.Equal(1800m, lines.Sum(l => l.Borc) - lines.Sum(l => l.Alacak));
    }

    [Fact]
    public async Task Gelir_gider_summary_and_breakdown_match()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedKnownLedgerAsync(scope, Guid.NewGuid());
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        var gg = await reports.GetGelirGiderAsync();
        Assert.Equal(5000m, gg.GelirToplam);      // satış net
        Assert.Equal(1000m, gg.GiderToplam);      // gider net
        Assert.Equal(1000m, gg.KdvTahsil);        // satış kdv (alacak)
        Assert.Equal(200m, gg.KdvIndirilecek);    // gider kdv (borç)
        Assert.Equal(4000m, gg.NetKar);           // 5000 − 1000

        var gelir = Assert.Single(gg.GelirKirilim);
        Assert.Equal("AracSatis", gelir.SourceType);
        Assert.Equal(5000m, gelir.Tutar);
        var gider = Assert.Single(gg.GiderKirilim);
        Assert.Equal("Gider", gider.SourceType);
        Assert.Equal(1000m, gider.Tutar);
    }

    [Fact]
    public async Task Date_range_filter_excludes_out_of_range()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        // Ocak'ta bir tahsilat.
        await cash.CollectAsync(new CashInput
        { CariId = Guid.NewGuid(), Tutar = 500m, Tarih = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) });

        // Şubat'tan itibaren sorgu → Ocak işlemi sayılmaz.
        var feb = await reports.GetKasaBankaSummaryAsync(
            from: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), to: null);
        Assert.Equal(0m, feb.KasaGiris);

        // Ocak'ı kapsayan aralık → görünür.
        var jan = await reports.GetKasaBankaSummaryAsync(
            from: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            to: new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero));
        Assert.Equal(500m, jan.KasaGiris);
    }

    [Fact]
    public async Task Reports_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await SeedKnownLedgerAsync(s1, Guid.NewGuid());

        using var s2 = host.ScopeFor(t2);
        var reports = s2.ServiceProvider.GetRequiredService<ReportService>();
        var s = await reports.GetKasaBankaSummaryAsync();
        var gg = await reports.GetGelirGiderAsync();
        Assert.Equal(0m, s.KasaGiris);
        Assert.Equal(0m, s.KasaCikis);
        Assert.Equal(0m, gg.GelirToplam);
        Assert.Equal(0m, gg.GiderToplam);
        Assert.Empty(await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa));
    }
}
