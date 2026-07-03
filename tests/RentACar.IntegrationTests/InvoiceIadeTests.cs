using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// İade faturası (küçük borç, PARA). BAĞIMSIZ ORACLE: kaynak fatura net 1000 + KDV 200 = brüt 1200;
/// iade TERS kayıt (Alacak Cari / Borç Gelir / Borç KDV) → iade sonrası cari 0, gelir 0, KDV-tahsil 0,
/// KDV-indirilecek 0 (iade indirim SAYILMAZ), defter global dengeli, tüm gelir/KDV raporları netleşir.
/// </summary>
[Collection("postgres")]
public sealed class InvoiceIadeTests(PostgresFixture fx)
{
    private static Task<Guid> Cari(IServiceProvider sp)
        => sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "İade Cari" });

    private static async Task<(decimal debit, decimal credit)> LedgerBalanceAsync(IServiceProvider sp)
    {
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
        return (entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase),
                entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase));
    }

    [Fact]
    public async Task Iade_ters_kayitla_geri_alir_ve_tum_raporlar_netlesir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cariId = await Cari(sp);
        var inv = sp.GetRequiredService<InvoiceService>();
        var cash = sp.GetRequiredService<CashService>();
        var rep = sp.GetRequiredService<ReportService>();

        var srcId = await inv.CreateManualAsync(new ManualInvoiceInput { CariId = cariId, NetTutar = 1000m, KdvOrani = 0.20m, Aciklama = "Kira" });
        Assert.Equal(1200m, await cash.GetCariBalanceAsync(cariId)); // önce: cari borçlu 1200

        var iadeId = await inv.CreateIadeAsync(srcId);

        // İade faturası doğru kuruldu (kaynak yansıması + RentalId null)
        var iade = await inv.GetAsync(iadeId);
        Assert.True(iade!.IadeMi);
        Assert.Equal(srcId, iade.KaynakFaturaId);
        Assert.Null(iade.RentalId);
        Assert.Equal(1000m, iade.NetTutar);
        Assert.Equal(200m, iade.KdvTutar);
        Assert.Equal(1200m, iade.GenelToplam);

        // ORACLE: iade sonrası her şey net 0
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cariId)); // 1200 borç − 1200 alacak

        var gg = await rep.GetGelirGiderAsync();
        Assert.Equal(0m, gg.GelirToplam);      // 1000 − 1000
        Assert.Equal(0m, gg.KdvTahsil);        // 200 − 200
        Assert.Equal(0m, gg.KdvIndirilecek);   // iade Borç KDV, indirime YAZILMADI

        var kdv = await rep.GetKdvListesiAsync();
        Assert.Equal(0m, kdv.ToplamKdv);       // fatura +200, iade −200
        Assert.Equal(0m, kdv.ToplamNet);

        var tf = await rep.GetTahsilatFaturaAsync();
        Assert.Equal(0m, tf.FaturaToplam);     // 1200 − 1200

        // Defter global dengeli (6 satır: 3 fatura + 3 iade), Σ borç == Σ alacak.
        var (debit, credit) = await LedgerBalanceAsync(sp);
        Assert.Equal(debit, credit);
        Assert.Equal(2400m, debit);            // 1200 (fatura Borç Cari) + 1000+200 (iade Borç Gelir+KDV)

        // İade defter satırları cari ekstrede "İade" etiketli (adversarial Low fix — "Fatura" değil).
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var iadeSatir = await db.AccountLedgerEntries.AsNoTracking()
                .Where(e => e.SourceType == "FaturaIade").ToListAsync();
            Assert.NotEmpty(iadeSatir);
            Assert.All(iadeSatir, e => Assert.StartsWith("İade", e.Description));
        }
    }

    [Fact]
    public async Task Cift_iade_ve_iadenin_iadesi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cariId = await Cari(sp);
        var inv = sp.GetRequiredService<InvoiceService>();

        var srcId = await inv.CreateManualAsync(new ManualInvoiceInput { CariId = cariId, NetTutar = 500m, KdvOrani = 0.20m });
        var iadeId = await inv.CreateIadeAsync(srcId);

        await Assert.ThrowsAsync<ValidationException>(() => inv.CreateIadeAsync(srcId));   // aynı fatura ikinci kez
        await Assert.ThrowsAsync<ValidationException>(() => inv.CreateIadeAsync(iadeId));  // iadenin iadesi
        await Assert.ThrowsAsync<ValidationException>(() => inv.CreateIadeAsync(Guid.NewGuid())); // olmayan fatura
    }

    [Fact]
    public async Task Iade_donem_kilidine_tabi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        Guid srcId;
        using (var s1 = host.ScopeFor(tenant))
        {
            var cariId = await Cari(s1.ServiceProvider);
            srcId = await s1.ServiceProvider.GetRequiredService<InvoiceService>()
                .CreateManualAsync(new ManualInvoiceInput { CariId = cariId, NetTutar = 500m });
        }
        // Kilit "o tarihe kadar (dahil) her şey kapalı" (bkz. InvoiceManualTests) → 2099 bugünü de kapatır.
        using (var s2 = host.ScopeFor(tenant))
            await s2.ServiceProvider.GetRequiredService<RentACar.Application.Periods.DonemKilidiService>()
                .LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

        using var s3 = host.ScopeFor(tenant); // taze scope = üretimde ayrı istek → kilidi okur
        await Assert.ThrowsAsync<ValidationException>(() =>
            s3.ServiceProvider.GetRequiredService<InvoiceService>().CreateIadeAsync(srcId));
    }
}
