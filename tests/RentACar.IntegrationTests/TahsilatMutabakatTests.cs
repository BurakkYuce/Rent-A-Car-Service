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
/// FAZ-68 — Tahsilat raporu SÖZLEŞME-SATIRI mutabakat modu.
///
/// <para><b>Bağımsız oracle:</b> her senaryonun borç/faturalanan/tahsilat üçlüsü testte ELLE
/// hesaplanır (ör. "3 gün × 1.000 = 3.000 → tam tahsilat → bakiye 0").</para>
///
/// <para><b>Raporun asıl işi "Ayrım" kolonudur:</b> sözleşme satırında tutulan <c>Tahsilat</c> ile
/// kasa hareketlerinden yeniden toplanan tutar normalde EŞİTTİR. Bu test, üç farklı senaryoda
/// (tam / kısmi / ters kayıtlı) ayrımın sıfır kaldığını doğrular — yani rapor sağlıklı bir veride
/// yanlış alarm ÜRETMEZ. Aksi hâlde kolon gürültüye boğulur ve gerçek tutarsızlık görünmez.</para>
///
/// <para>Dönem-toplamı modunun REGRESYONSUZ kaldığı da ayrıca doğrulanır.</para>
/// </summary>
[Collection("postgres")]
public sealed class TahsilatMutabakatTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T0 = TestZaman.Now().AddDays(-20);

    private static async Task<(Guid kira, Guid cari)> RentalAsync(
        IServiceProvider sp, string title, string plate, decimal daily, int day)
    {
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = title });
        var vehicle = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate });
        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = account, VehicleId = vehicle, BasTar = T0, BitTar = T0.AddDays(day),
            GunlukUcret = daily
        });
        return (kira: rental, cari: account);
    }

    [Fact]
    public async Task Uc_senaryo_ELLE_HESAPLANAN_borc_fatura_tahsilat_uclusunu_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var inv = sp.GetRequiredService<InvoiceService>();
        var cash = sp.GetRequiredService<CashService>();

        // A: 3 gün × 1.000 = 3.000 borç → faturalandı → TAM tahsil edildi.
        var (a, accountA) = await RentalAsync(sp, "Alfa", "34 MT 01", 1000m, 3);
        await inv.CreateFromRentalAsync(a);
        var aTop = (await sp.GetRequiredService<RentalService>().GetAsync(a))!.GenelToplam;
        await cash.CollectAsync(new CashInput { CariId = accountA, Tutar = aTop, RentalId = a });

        // B: 2 gün × 500 = 1.000 borç → faturalandı → YARISI tahsil edildi.
        var (b, accountB) = await RentalAsync(sp, "Beta", "34 MT 02", 500m, 2);
        await inv.CreateFromRentalAsync(b);
        var bTop = (await sp.GetRequiredService<RentalService>().GetAsync(b))!.GenelToplam;
        await cash.CollectAsync(new CashInput { CariId = accountB, Tutar = bTop / 2m, RentalId = b });

        // C: 1 gün × 700 → HİÇ faturalanmadı, hiç tahsil edilmedi.
        var (c, _) = await RentalAsync(sp, "Gama", "34 MT 03", 700m, 1);

        var rows = await sp.GetRequiredService<ReportService>().GetCollectionReconciliationAsync();
        Assert.Equal(3, rows.Count);

        var ra = Assert.Single(rows, x => x.RentalId == a);
        Assert.Equal(aTop, ra.GenelToplam);
        Assert.Equal(aTop, ra.Faturalanan);          // tamamı faturalandı
        Assert.Equal(0m, ra.FaturaFarki);
        Assert.Equal(aTop, ra.Tahsilat);             // tamamı tahsil edildi
        Assert.Equal(0m, ra.Bakiye);
        Assert.Equal(0m, ra.TahsilatAyrimi);         // sözleşme ↔ kasa uyumlu
        Assert.False(ra.Tutarsiz);

        var rb = Assert.Single(rows, x => x.RentalId == b);
        Assert.Equal(bTop, rb.Faturalanan);
        Assert.Equal(bTop / 2m, rb.Tahsilat);
        Assert.Equal(bTop / 2m, rb.Bakiye);          // yarısı açık
        Assert.Equal(0m, rb.TahsilatAyrimi);

        var rc = Assert.Single(rows, x => x.RentalId == c);
        Assert.Equal(0m, rc.Faturalanan);            // hiç faturalanmadı
        Assert.Equal(rc.GenelToplam, rc.FaturaFarki);
        Assert.Equal(0m, rc.Tahsilat);
        Assert.Equal(rc.GenelToplam, rc.Bakiye);
        Assert.Equal(0m, rc.TahsilatAyrimi);
    }

    [Fact]
    public async Task Ters_kayit_sonrasi_AYRIM_yine_SIFIR()
    {
        // Ters kayıt hem sözleşme Tahsilat alanını hem kasa hareketlerini etkiler; ikisi aynı
        // kuraldan geçtiği için ayrım sıfır kalmalı. Rapor yanlış alarm üretmemeli.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();

        var (rental, account) = await RentalAsync(sp, "Delta", "34 MT 10", 1000m, 2);
        var txId = await cash.CollectAsync(new CashInput { CariId = account, Tutar = 800m, RentalId = rental });
        await cash.ReverseAsync(txId);

        var row = Assert.Single(await sp.GetRequiredService<ReportService>().GetCollectionReconciliationAsync());
        // 800 alındı, 800 geri alındı → net tahsilat 0.
        Assert.Equal(0m, row.Tahsilat);
        Assert.Equal(0m, row.TahsilatAyrimi);
        Assert.False(row.Tutarsiz);
    }

    [Fact]
    public async Task Iade_faturasi_FATURALANANI_dusurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var inv = sp.GetRequiredService<InvoiceService>();

        var (rental, _) = await RentalAsync(sp, "Epsilon", "34 MT 20", 1000m, 2);
        var invoiceId = await inv.CreateFromRentalAsync(rental);
        var top = (await sp.GetRequiredService<RentalService>().GetAsync(rental))!.GenelToplam;

        var once = Assert.Single(await sp.GetRequiredService<ReportService>().GetCollectionReconciliationAsync());
        Assert.Equal(top, once.Faturalanan);

        await inv.CreateRefundAsync(invoiceId);

        // İade sonrası faturalanan SIFIRA döner (iade-netli) → fatura farkı tüm borç kadar.
        var after = Assert.Single(await sp.GetRequiredService<ReportService>().GetCollectionReconciliationAsync());
        Assert.Equal(0m, after.Faturalanan);
        Assert.Equal(top, after.FaturaFarki);
    }

    [Fact]
    public async Task Filtreler_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var report = sp.GetRequiredService<ReportService>();
        var cash = sp.GetRequiredService<CashService>();

        var (a, accountA) = await RentalAsync(sp, "Alfa Lojistik", "34 FL 01", 1000m, 2);
        var (b, _) = await RentalAsync(sp, "Beta Turizm", "06 BT 02", 500m, 1);

        // A'yı tamamen tahsil et → bakiyesi kapanır.
        var aTop = (await sp.GetRequiredService<RentalService>().GetAsync(a))!.GenelToplam;
        await cash.CollectAsync(new CashInput { CariId = accountA, Tutar = aTop, RentalId = a });

        Assert.Equal(2, (await report.GetCollectionReconciliationAsync()).Count);
        Assert.Equal(2, (await report.GetCollectionReconciliationAsync(new TahsilatMutabakatFilter())).Count);

        // Metin: müşteri adı / plaka (boşluklu giriş de bulmalı) / sözleşme no
        Assert.Equal(a, Assert.Single(await report.GetCollectionReconciliationAsync(
            new TahsilatMutabakatFilter { Ara = "alfa" })).RentalId);
        Assert.Equal(b, Assert.Single(await report.GetCollectionReconciliationAsync(
            new TahsilatMutabakatFilter { Ara = "06 BT" })).RentalId);

        // Bakiye durumu
        Assert.Equal(a, Assert.Single(await report.GetCollectionReconciliationAsync(
            new TahsilatMutabakatFilter { BakiyeDurumu = "kapali" })).RentalId);
        Assert.Equal(b, Assert.Single(await report.GetCollectionReconciliationAsync(
            new TahsilatMutabakatFilter { BakiyeDurumu = "acik" })).RentalId);

        // Durum
        Assert.Equal(2, (await report.GetCollectionReconciliationAsync(
            new TahsilatMutabakatFilter { Durum = RentalStatus.Kirada })).Count);
        Assert.Empty(await report.GetCollectionReconciliationAsync(
            new TahsilatMutabakatFilter { Durum = RentalStatus.Iptal }));

        // Sağlıklı veride "yalnız tutarsız" BOŞ dönmeli (yanlış alarm yok).
        Assert.Empty(await report.GetCollectionReconciliationAsync(
            new TahsilatMutabakatFilter { YalnizTutarsiz = true }));

        // Tarih aralığı (kira başlangıcına göre)
        Assert.Equal(2, (await report.GetCollectionReconciliationAsync(
            new TahsilatMutabakatFilter { Bas = T0.AddDays(-1) })).Count);
        Assert.Empty(await report.GetCollectionReconciliationAsync(
            new TahsilatMutabakatFilter { Bit = T0.AddDays(-1) }));
    }

    [Fact]
    public async Task Donem_toplami_modu_REGRESYONSUZ()
    {
        // Satır modu eklendi diye eski dönem-toplamı görünümü değişmemeli.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (rental, account) = await RentalAsync(sp, "Zeta", "34 MT 30", 1000m, 2);
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rental);
        var top = (await sp.GetRequiredService<RentalService>().GetAsync(rental))!.GenelToplam;
        await sp.GetRequiredService<CashService>()
            .CollectAsync(new CashInput { CariId = account, Tutar = top, RentalId = rental });

        var summary = await sp.GetRequiredService<ReportService>().GetCollectionInvoiceAsync();
        Assert.Equal(1, summary.FaturaAdet);
        Assert.Equal(top, summary.FaturaToplam);
        Assert.Equal(1, summary.TahsilatAdet);
        Assert.Equal(top, summary.TahsilatToplam);
        Assert.Equal(0m, summary.Fark);
    }

    [Fact]
    public async Task Mutabakat_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await RentalAsync(s1.ServiceProvider, "Gizli", "34 GZ 99", 1000m, 1);

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ReportService>().GetCollectionReconciliationAsync());
    }
}
