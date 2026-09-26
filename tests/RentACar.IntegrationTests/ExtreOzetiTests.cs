using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-61 — Extre özeti (fatura seviyesinde müşteri + plaka + vade görünümü).
///
/// <para><b>Bu ekranın en kolay yanlış anlaşılacak yanı:</b> tutarlar BRÜTTÜR. Sistemde
/// fatura-bazlı tahsilat mahsubu yok (tahsilat cari bakiyesine yazılır, faturaya kapatılmaz), bu
/// yüzden "açık tutar" diye bir veri üretilmiyor. Aşağıdaki test bunu açıkça kanıtlıyor: bir
/// faturayı tam tahsil ettikten SONRA bile satır brüt tutarıyla listede kalıyor, buna karşılık
/// cari bakiye sıfırlanıyor. İki ekranın farklı soruları cevapladığı böylece kilitleniyor.</para>
///
/// <para>Bağımsız oracle: tutarlar ve satır sayıları testte elle hesaplanır.</para>
/// </summary>
[Collection("postgres")]
public sealed class ExtreOzetiTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T0 = TestZaman.Now();

    private static async Task<Guid> CustomerAsync(IServiceProvider sp, string title)
        => await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = title });

    private static Task<Guid> InvoiceAsync(
        IServiceProvider sp, Guid account, decimal net, DateTimeOffset? date = null, DateTimeOffset? due = null)
        => sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = account, NetTutar = net, KdvOrani = 0.20m, Tarih = date, VadeTarihi = due });

    [Fact]
    public async Task Satirlar_vade_sirasinda_ve_brut_tutarla_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var a = await CustomerAsync(sp, "Alfa");
        var b = await CustomerAsync(sp, "Beta");

        // ELLE: 3 fatura. Net 1000/2000/500 @%20 → brüt 1200/2400/600.
        await InvoiceAsync(sp, a, 1000m, T0.AddDays(-20), T0.AddDays(-5));   // vadesi GEÇMİŞ
        await InvoiceAsync(sp, b, 2000m, T0.AddDays(-10), T0.AddDays(10));   // vadesi GELECEK
        await InvoiceAsync(sp, a, 500m, T0.AddDays(-2));                      // VADESİZ

        var rows = await sp.GetRequiredService<ReportService>().GetStatementSummaryAsync(asOf: T0);
        Assert.Equal(3, rows.Count);

        // Sıralama: vadesi olanlar önce (en erken vade üstte), vadesizler sonda.
        Assert.Equal(T0.AddDays(-5), rows[0].VadeTarihi);
        Assert.Equal(T0.AddDays(10), rows[1].VadeTarihi);
        Assert.Null(rows[2].VadeTarihi);

        Assert.Equal(1200m, rows[0].Tutar);
        Assert.Equal(2400m, rows[1].Tutar);
        Assert.Equal(600m, rows[2].Tutar);

        // Kalan gün: −5 (gecikmiş), +10, vadesiz → null.
        Assert.Equal(-5, rows[0].RemainingDays(T0));
        Assert.Equal(10, rows[1].RemainingDays(T0));
        Assert.Null(rows[2].RemainingDays(T0));

        // Brüt toplam: 1200 + 2400 + 600 = 4200.
        Assert.Equal(4200m, rows.Sum(r => r.IsaretliTutarTl));
    }

    [Fact]
    public async Task TAHSIL_EDILEN_fatura_listede_KALIR_cari_bakiye_ise_SIFIRLANIR()
    {
        // Bu testin amacı iki ekranın farkını kanıtlamak: extre özeti fatura-brüt, cari bakiye net.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var account = await CustomerAsync(sp, "Gama");
        var cash = sp.GetRequiredService<CashService>();

        await InvoiceAsync(sp, account, 1000m, T0.AddDays(-10), T0.AddDays(-1));   // brüt 1200
        Assert.Equal(1200m, await cash.GetAccountBalanceAsync(account));

        // Faturayı TAM tahsil et.
        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 1200m });
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(account));

        // Extre özeti hâlâ 1.200 brüt gösterir — fatura-bazlı mahsup YOK.
        var row = Assert.Single(await sp.GetRequiredService<ReportService>().GetStatementSummaryAsync(asOf: T0));
        Assert.Equal(1200m, row.Tutar);

        // Cari bakiye raporu ise carinin kapandığını gösterir (sıfır bakiyeli cari listelenmez).
        Assert.Empty(await sp.GetRequiredService<ReportService>().GetAccountBalancesAsync());
    }

    [Fact]
    public async Task Iade_faturasi_NEGATIF_iptal_faturasi_HIC_gorunmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var inv = sp.GetRequiredService<InvoiceService>();
        var account = await CustomerAsync(sp, "Delta");

        var f1 = await InvoiceAsync(sp, account, 1000m, T0.AddDays(-10), T0.AddDays(5));   // brüt 1200
        await inv.CreateRefundAsync(f1);                                                  // iade: brüt 1200

        var rows = await sp.GetRequiredService<ReportService>().GetStatementSummaryAsync(asOf: T0);
        Assert.Equal(2, rows.Count);

        var refund = Assert.Single(rows, r => r.IadeMi);
        Assert.Equal(1200m, refund.Tutar);              // DB'de pozitif saklanıyor
        Assert.Equal(-1200m, refund.IsaretliTutar);     // …ama toplamda negatif
        // Net etki sıfır: 1200 − 1200 = 0. İşaret verilmeseydi 2.400 çıkardı.
        Assert.Equal(0m, rows.Sum(r => r.IsaretliTutarTl));
    }

    [Fact]
    public async Task Kiraya_bagli_faturada_plaka_ve_ofis_cozulur_manuel_fatura_DUSMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var account = await CustomerAsync(sp, "Epsilon");
        var vehicle = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 EO 01" });

        var start = T0.AddDays(-8);
        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = account, VehicleId = vehicle, BasTar = start, BitTar = start.AddDays(2),
            GunlukUcret = 1000m, CikisOfisi = "Merkez Ofis"
        });
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rental);
        await InvoiceAsync(sp, account, 100m);   // kirasız manuel fatura

        var rows = await sp.GetRequiredService<ReportService>().GetStatementSummaryAsync(asOf: T0);
        Assert.Equal(2, rows.Count);   // manuel fatura JOIN'de DÜŞMEDİ (LEFT JOIN)

        var rentalRow = Assert.Single(rows, r => r.Plaka is not null);
        Assert.Equal("34EO01", rentalRow.Plaka);
        Assert.Equal("Merkez Ofis", rentalRow.CikisOfisi);
        Assert.False(string.IsNullOrWhiteSpace(rentalRow.SozlesmeNo));

        var manual = Assert.Single(rows, r => r.Plaka is null);
        Assert.Null(manual.CikisOfisi);
        Assert.Null(manual.SozlesmeNo);
    }

    [Fact]
    public async Task Filtreler_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var report = sp.GetRequiredService<ReportService>();
        var a = await CustomerAsync(sp, "Alfa");
        var b = await CustomerAsync(sp, "Beta");

        await InvoiceAsync(sp, a, 1000m, T0.AddDays(-30), T0.AddDays(-15));  // gecikmiş
        await InvoiceAsync(sp, b, 2000m, T0.AddDays(-5), T0.AddDays(20));    // gelecek vadeli
        await InvoiceAsync(sp, a, 300m, T0.AddDays(-1));                      // vadesiz

        Assert.Equal(3, (await report.GetStatementSummaryAsync(asOf: T0)).Count);
        Assert.Equal(3, (await report.GetStatementSummaryAsync(new ExtreOzetiFilter(), T0)).Count);

        // Cari
        Assert.Equal(2, (await report.GetStatementSummaryAsync(new ExtreOzetiFilter { CariId = a }, T0)).Count);
        Assert.Single(await report.GetStatementSummaryAsync(new ExtreOzetiFilter { CariId = b }, T0));

        // Yalnız gecikmiş: VADESİZ kayıtlar da düşer (vade yoksa gecikme kavramı yok).
        var overdue = await report.GetStatementSummaryAsync(new ExtreOzetiFilter { YalnizGecikmis = true }, T0);
        Assert.Equal(T0.AddDays(-15), Assert.Single(overdue).VadeTarihi);

        // Fatura tarihi aralığı: son 10 gün → 2 kayıt.
        Assert.Equal(2, (await report.GetStatementSummaryAsync(new ExtreOzetiFilter { Bas = T0.AddDays(-10) }, T0)).Count);

        // Kirasız faturalar plaka/ofis filtresinde elenir.
        Assert.Empty(await report.GetStatementSummaryAsync(new ExtreOzetiFilter { Plaka = "34" }, T0));
        Assert.Empty(await report.GetStatementSummaryAsync(new ExtreOzetiFilter { Ofis = "Merkez Ofis" }, T0));
    }

    [Fact]
    public async Task Extre_ozeti_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await InvoiceAsync(s1.ServiceProvider, await CustomerAsync(s1.ServiceProvider, "Gizli"), 999m);

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ReportService>().GetStatementSummaryAsync(asOf: T0));
    }
}
