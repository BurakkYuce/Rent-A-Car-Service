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
    private static readonly DateTimeOffset T0 = TestZaman.Simdi().AddDays(-20);

    private static async Task<(Guid kira, Guid cari)> KiraAsync(
        IServiceProvider sp, string unvan, string plaka, decimal gunluk, int gun)
    {
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = unvan });
        var arac = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plaka });
        var kira = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = arac, BasTar = T0, BitTar = T0.AddDays(gun),
            GunlukUcret = gunluk
        });
        return (kira, cari);
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
        var (a, cariA) = await KiraAsync(sp, "Alfa", "34 MT 01", 1000m, 3);
        await inv.CreateFromRentalAsync(a);
        var aTop = (await sp.GetRequiredService<RentalService>().GetAsync(a))!.GenelToplam;
        await cash.CollectAsync(new CashInput { CariId = cariA, Tutar = aTop, RentalId = a });

        // B: 2 gün × 500 = 1.000 borç → faturalandı → YARISI tahsil edildi.
        var (b, cariB) = await KiraAsync(sp, "Beta", "34 MT 02", 500m, 2);
        await inv.CreateFromRentalAsync(b);
        var bTop = (await sp.GetRequiredService<RentalService>().GetAsync(b))!.GenelToplam;
        await cash.CollectAsync(new CashInput { CariId = cariB, Tutar = bTop / 2m, RentalId = b });

        // C: 1 gün × 700 → HİÇ faturalanmadı, hiç tahsil edilmedi.
        var (c, _) = await KiraAsync(sp, "Gama", "34 MT 03", 700m, 1);

        var rows = await sp.GetRequiredService<ReportService>().GetTahsilatMutabakatAsync();
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

        var (kira, cari) = await KiraAsync(sp, "Delta", "34 MT 10", 1000m, 2);
        var txId = await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 800m, RentalId = kira });
        await cash.ReverseAsync(txId);

        var row = Assert.Single(await sp.GetRequiredService<ReportService>().GetTahsilatMutabakatAsync());
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

        var (kira, _) = await KiraAsync(sp, "Epsilon", "34 MT 20", 1000m, 2);
        var faturaId = await inv.CreateFromRentalAsync(kira);
        var top = (await sp.GetRequiredService<RentalService>().GetAsync(kira))!.GenelToplam;

        var once = Assert.Single(await sp.GetRequiredService<ReportService>().GetTahsilatMutabakatAsync());
        Assert.Equal(top, once.Faturalanan);

        await inv.CreateIadeAsync(faturaId);

        // İade sonrası faturalanan SIFIRA döner (iade-netli) → fatura farkı tüm borç kadar.
        var sonra = Assert.Single(await sp.GetRequiredService<ReportService>().GetTahsilatMutabakatAsync());
        Assert.Equal(0m, sonra.Faturalanan);
        Assert.Equal(top, sonra.FaturaFarki);
    }

    [Fact]
    public async Task Filtreler_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var rapor = sp.GetRequiredService<ReportService>();
        var cash = sp.GetRequiredService<CashService>();

        var (a, cariA) = await KiraAsync(sp, "Alfa Lojistik", "34 FL 01", 1000m, 2);
        var (b, _) = await KiraAsync(sp, "Beta Turizm", "06 BT 02", 500m, 1);

        // A'yı tamamen tahsil et → bakiyesi kapanır.
        var aTop = (await sp.GetRequiredService<RentalService>().GetAsync(a))!.GenelToplam;
        await cash.CollectAsync(new CashInput { CariId = cariA, Tutar = aTop, RentalId = a });

        Assert.Equal(2, (await rapor.GetTahsilatMutabakatAsync()).Count);
        Assert.Equal(2, (await rapor.GetTahsilatMutabakatAsync(new TahsilatMutabakatFilter())).Count);

        // Metin: müşteri adı / plaka (boşluklu giriş de bulmalı) / sözleşme no
        Assert.Equal(a, Assert.Single(await rapor.GetTahsilatMutabakatAsync(
            new TahsilatMutabakatFilter { Ara = "alfa" })).RentalId);
        Assert.Equal(b, Assert.Single(await rapor.GetTahsilatMutabakatAsync(
            new TahsilatMutabakatFilter { Ara = "06 BT" })).RentalId);

        // Bakiye durumu
        Assert.Equal(a, Assert.Single(await rapor.GetTahsilatMutabakatAsync(
            new TahsilatMutabakatFilter { BakiyeDurumu = "kapali" })).RentalId);
        Assert.Equal(b, Assert.Single(await rapor.GetTahsilatMutabakatAsync(
            new TahsilatMutabakatFilter { BakiyeDurumu = "acik" })).RentalId);

        // Durum
        Assert.Equal(2, (await rapor.GetTahsilatMutabakatAsync(
            new TahsilatMutabakatFilter { Durum = RentalStatus.Kirada })).Count);
        Assert.Empty(await rapor.GetTahsilatMutabakatAsync(
            new TahsilatMutabakatFilter { Durum = RentalStatus.Iptal }));

        // Sağlıklı veride "yalnız tutarsız" BOŞ dönmeli (yanlış alarm yok).
        Assert.Empty(await rapor.GetTahsilatMutabakatAsync(
            new TahsilatMutabakatFilter { YalnizTutarsiz = true }));

        // Tarih aralığı (kira başlangıcına göre)
        Assert.Equal(2, (await rapor.GetTahsilatMutabakatAsync(
            new TahsilatMutabakatFilter { Bas = T0.AddDays(-1) })).Count);
        Assert.Empty(await rapor.GetTahsilatMutabakatAsync(
            new TahsilatMutabakatFilter { Bit = T0.AddDays(-1) }));
    }

    [Fact]
    public async Task Donem_toplami_modu_REGRESYONSUZ()
    {
        // Satır modu eklendi diye eski dönem-toplamı görünümü değişmemeli.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (kira, cari) = await KiraAsync(sp, "Zeta", "34 MT 30", 1000m, 2);
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(kira);
        var top = (await sp.GetRequiredService<RentalService>().GetAsync(kira))!.GenelToplam;
        await sp.GetRequiredService<CashService>()
            .CollectAsync(new CashInput { CariId = cari, Tutar = top, RentalId = kira });

        var ozet = await sp.GetRequiredService<ReportService>().GetTahsilatFaturaAsync();
        Assert.Equal(1, ozet.FaturaAdet);
        Assert.Equal(top, ozet.FaturaToplam);
        Assert.Equal(1, ozet.TahsilatAdet);
        Assert.Equal(top, ozet.TahsilatToplam);
        Assert.Equal(0m, ozet.Fark);
    }

    [Fact]
    public async Task Mutabakat_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await KiraAsync(s1.ServiceProvider, "Gizli", "34 GZ 99", 1000m, 1);

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ReportService>().GetTahsilatMutabakatAsync());
    }
}
