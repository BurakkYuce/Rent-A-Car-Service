using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Filo Analiz adversarial senaryoları (bağımsız ajan probe'larından kalıcılaştırıldı; D/E düzeltme
/// SONRASI davranışı kilitler). A: silinmiş araç (kalıntı satır + mutabakat + kohort-dışı);
/// B: karne↔filo KPI paritesi (zengin satılmış araç, elle sağlamalı) — iki kod yolu drift kilidi;
/// C: pencereli invariant (karışık pencere + manuel fatura → GelirGider mutabakatı);
/// D: pencere-dışı hareketli ve hiç-hareketsiz araç PANODA GÖRÜNÜR (filo-tohumlama);
/// E: yaş kovası gün-hassas; E2: gelecek alım tarihi clamp; F: keyfi sıralama string.
/// </summary>
[Collection("postgres")]
public sealed class FiloAnalizAdversarialTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset FleetEntry = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RentalStart = new(2025, 1, 10, 9, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> RecourseAsync(IServiceProvider sp, Guid vehicle, Guid account, decimal cost,
        DateTimeOffset? date = null)
    {
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = vehicle, Tip = ServiceType.Ariza, GirisKm = 0,
            HasarSorumlu = DamageResponsible.Musteri, KusurOrani = 0.5m,
            Lines = [new ServiceLineInput { Aciklama = "Onarım", Tutar = cost }]
        });
        await svc.StartAsync(id);
        await svc.CompleteAsync(id, pickupKm: 10);
        await svc.ReflectAsync(id, account, date: date);
        return id;
    }

    // ---------- A: silinmiş araç (VehicleList "Sil") ----------
    [Fact]
    public async Task ProbeA_silinmis_arac_crash_yok_toplam_mutabik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var v = await veh.CreateAsync(new VehicleInput
        { Plaka = "34 DEL 1", AlimBedeli = 1000m, AlimTarihi = DateTimeOffset.UtcNow.AddMonths(-6) });
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = v, NetTutar = 100m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit });

        Assert.True(await veh.DeleteAsync(v)); // hard delete; defter (immutable) kalır

        var rs = sp.GetRequiredService<ReportService>();
        var d = await rs.GetFleetAnalysisAsync(); // crash?
        var row = Assert.Single(d.Satirlar);
        Assert.Equal(v, row.VehicleId);
        Assert.Equal("(bilinmeyen araç)", row.Plaka);   // Karlilik ile aynı yol
        Assert.Equal(100m, row.Gider);
        Assert.Null(row.DolulukYuzde);
        Assert.Null(row.RoiYuzde);                       // AlimBedeli kayboldu → null
        Assert.Null(row.YasAy);                          // kohort: "Alım tarihi yok"
        Assert.Equal(0, row.SahiplikGun);

        // İnvaryant 1: toplam defterle mutabık
        var gg = await rs.GetRevenueExpenseAsync();
        Assert.Equal(gg.GiderToplam, d.ToplamGider);
        Assert.Equal(100m, d.ToplamGider);
        Assert.Empty(d.YasKohortu);   // silinmiş araç kohorta girmez (yalnız mevcut filo sayılır)

        // Karne aynı id: null (dashboard'daki plaka linki 404'e gider — UX notu)
        Assert.Null(await rs.GetVehicleScorecardAsync(v));

        // F (ayrıca): keyfi sıralama string patlamaz + default'a düşer; tüm-null doluluk sıralaması patlamaz
        var garbage = await rs.GetFleetAnalysisAsync(sort: "  DROP TABLE; ");
        Assert.Single(garbage.Satirlar);
        var dol = await rs.GetFleetAnalysisAsync(sort: "doluluk"); // tek satır, DolulukYuzde null
        Assert.Single(dol.Satirlar);
    }

    // ---------- B: karne-vs-filo KPI paritesi (zengin, SATILMIŞ araç) ----------
    [Fact]
    public async Task ProbeB_karne_ve_filo_kpi_birebir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var now = DateTimeOffset.UtcNow;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        {
            Plaka = "34 ZP 01", AlimBedeli = 1000m, IkinciElDeger = 800m,
            AlimTarihi = now.AddMonths(-20), FiloGirisTarih = FleetEntry
        });
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Zengin", Soyad = "Cari" });

        // Kira 10–12 Oca (2g×100), 300 km, faturalı (net 166,67)
        var rentals = sp.GetRequiredService<RentalService>();
        var r = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = account, VehicleId = v, BasTar = RentalStart, BitTar = RentalStart.AddDays(2), GunlukUcret = 100m });
        await rentals.DeliverAsync(r, pickupKm: 1000, pickupFuel: 8);
        await rentals.ReturnAsync(r, returnKm: 1300, returnFuel: 8, RentalStart.AddDays(2));
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(r);

        // Rücu 620×0,5=310 gelir + gider 150
        await RecourseAsync(sp, v, account, 620m);
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = v, NetTutar = 150m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit });

        // TAMAMLANMIŞ satış 800 (31 Mart) → Satildi → sahiplik penceresi satışta biter
        await sp.GetRequiredService<VehicleSaleService>().CreateAsync(new VehicleSaleInput
        {
            VehicleId = v, AliciCariId = Guid.NewGuid(), SatisNet = 800m, KdvOrani = 0m,
            Doviz = "TRY", Kur = 1m, Tarih = new DateTimeOffset(2025, 3, 31, 12, 0, 0, TimeSpan.Zero)
        });

        var rs = sp.GetRequiredService<ReportService>();
        var scorecard = (await rs.GetVehicleScorecardAsync(v))!;
        var fleet = await rs.GetFleetAnalysisAsync();
        var row = fleet.Satirlar.Single(x => x.VehicleId == v);

        // İnvaryant 2: iki kod yolu birebir
        Assert.Equal(scorecard.Kpi.DolulukYuzde, row.DolulukYuzde);
        Assert.Equal(scorecard.Kpi.RoiYuzde, row.RoiYuzde);
        Assert.Equal(scorecard.Kpi.KmBasinaMaliyet, row.KmBasinaMaliyet);
        Assert.Equal(scorecard.Kpi.SahiplikGun, row.SahiplikGun);
        Assert.Equal(scorecard.Kpi.KiralananGun, row.KiralananGun);
        Assert.Equal(scorecard.ToplamGelir - scorecard.ToplamGider, row.NetKar);

        // Elle sağlama: gelir 166,67+310+800=1276,67; gider 150; net 1126,67;
        // ROI (satılmış, kapanış): (1126,67−1000)×100/1000 = 12,67; km-maliyet 150/300 = 0,50;
        // sahiplik 1 Oca–31 Mart = 90 gün; doluluk 3×100/90 = 3,33.
        Assert.Equal(1126.67m, row.NetKar);
        Assert.Equal(12.67m, row.RoiYuzde);
        Assert.Equal(0.50m, row.KmBasinaMaliyet);
        Assert.Equal(90, row.SahiplikGun);
        Assert.Equal(3.33m, row.DolulukYuzde);

        // Dar pencere (yalnız bugün ±1g): P&L daralır ama KPI ömür-boyu AYNI kalır (karne F2 dersi)
        var narrow = await rs.GetFleetAnalysisAsync(now.AddDays(-1), now.AddDays(1));
        var narrowRow = narrow.Satirlar.Single(x => x.VehicleId == v);
        Assert.Equal(row.RoiYuzde, narrowRow.RoiYuzde);
        Assert.Equal(row.DolulukYuzde, narrowRow.DolulukYuzde);
        Assert.Equal(row.KmBasinaMaliyet, narrowRow.KmBasinaMaliyet);
        // dar pencere P&L: bugün postlananlar (fatura 166,67 + rücu 310 + gider 150); satış (31 Mart) DIŞARIDA
        Assert.Equal(476.67m, narrowRow.Gelir);
        Assert.Equal(150m, narrowRow.Gider);
        // pencereli karne ile parite
        var scorecardNarrow = (await rs.GetVehicleScorecardAsync(v, now.AddDays(-1), now.AddDays(1)))!;
        Assert.Equal(scorecardNarrow.ToplamGelir, narrowRow.Gelir);
        Assert.Equal(scorecardNarrow.ToplamGider, narrowRow.Gider);
        Assert.Equal(scorecardNarrow.Kpi.RoiYuzde, narrowRow.RoiYuzde);
    }

    // ---------- C: pencereli invariant + manuel (atanamayan) fatura ----------
    [Fact]
    public async Task ProbeC_pencereli_toplam_gelirgider_ile_mutabik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddMonths(-3);
        var windowEnd = now.AddMonths(-1);
        var insideDate = now.AddMonths(-2);       // pencere İÇİ
        var outsideDate = now.AddMonths(-8);      // pencere DIŞI

        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 PC 01" });
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Pencere", Soyad = "Cari" });
        var exp = sp.GetRequiredService<ExpenseService>();

        // Araç gideri: 100 içeride, 50 dışarıda
        await exp.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = v, NetTutar = 100m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit, Tarih = insideDate });
        await exp.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = v, NetTutar = 50m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit, Tarih = outsideDate });
        // Genel gider (Atanmamış): 40 içeride
        await exp.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 40m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit, Tarih = insideDate });
        // Araca atfedilen gelir: rücu 310 içeride, 100 dışarıda
        await RecourseAsync(sp, v, account, 620m, insideDate);
        await RecourseAsync(sp, v, account, 200m, outsideDate);
        // MANUEL fatura (kaynak atfı yok → Atanmamış gelir): net 500 içeride
        await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = account, NetTutar = 500m, KdvOrani = 0m, Tarih = insideDate, Aciklama = "manuel" });

        var rs = sp.GetRequiredService<ReportService>();
        var d = await rs.GetFleetAnalysisAsync(windowStart, windowEnd);
        var gg = await rs.GetRevenueExpenseAsync(windowStart, windowEnd);

        // İnvaryant 1 (pencereli): satırlar + Atanmamış == defter
        Assert.Equal(gg.GelirToplam, d.ToplamGelir);
        Assert.Equal(gg.GiderToplam, d.ToplamGider);
        // Elle: pencere içi gelir 310 (rücu) + 500 (manuel) = 810; gider 100 + 40 = 140
        Assert.Equal(810m, d.ToplamGelir);
        Assert.Equal(140m, d.ToplamGider);
        Assert.Equal(500m, d.AtanmamisGelir);
        Assert.Equal(40m, d.AtanmamisGider);
        var row = d.Satirlar.Single(x => x.VehicleId == v);
        Assert.Equal(310m, row.Gelir);   // dışarıdaki 100 pencere P&L'inde yok
        Assert.Equal(100m, row.Gider);

        // Penceresiz de mutabık
        var d0 = await rs.GetFleetAnalysisAsync();
        var gg0 = await rs.GetRevenueExpenseAsync();
        Assert.Equal(gg0.GelirToplam, d0.ToplamGelir);
        Assert.Equal(gg0.GiderToplam, d0.ToplamGider);
        Assert.Equal(910m, d0.ToplamGelir);   // 310+100+500
        Assert.Equal(190m, d0.ToplamGider);   // 100+50+40
    }

    // ---------- D (düzeltildi): pano TÜM filodan tohumlanır ----------
    [Fact]
    public async Task ProbeD_tum_filo_panoda_gorunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var now = DateTimeOffset.UtcNow;

        // Araç: 8 ay önce 999 gider (zarar makinesi), pencerede hiçbir hareket yok
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 GH 01", AlimBedeli = 5000m, AlimTarihi = now.AddMonths(-9) });
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = v, NetTutar = 999m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit, Tarih = now.AddMonths(-8) });
        // Hiç defter hareketi olmayan araç (yeni alım — AlimBedeli defter yazmaz)
        var v2 = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 GH 02", AlimBedeli = 9000m, AlimTarihi = now.AddMonths(-1) });

        var rs = sp.GetRequiredService<ReportService>();
        // Düzeltme (F-D): aylık görünümde v pencere-dışı hareketli olsa da SATIRDA (0 P&L) — gizli
        // zarar makinesi panodan kaçmaz; ROI ömür-boyu kaybı yine gösterir.
        var monthly = await rs.GetFleetAnalysisAsync(now.AddMonths(-1), now);
        var vRow = monthly.Satirlar.Single(x => x.VehicleId == v);
        Assert.Equal(0m, vRow.Gelir);
        Assert.Equal(0m, vRow.Gider);
        Assert.Equal(-19.98m, vRow.RoiYuzde);                 // −999×100/5000 (ömür boyu, elle)
        // Hiç defter hareketi olmayan v2 de görünür (0 doluluk yakalanabilir).
        var v2Row = monthly.Satirlar.Single(x => x.VehicleId == v2);
        Assert.Equal(0m, v2Row.NetKar);
        // Kohort filo mevcudunu sayar: 2 araç.
        Assert.Equal(2, monthly.YasKohortu.Sum(k => k.AracAdet));
        // Toplamlar yine defterle mutabık (0-satırlar toplamı değiştirmez).
        Assert.Equal((await rs.GetRevenueExpenseAsync(now.AddMonths(-1), now)).GiderToplam, monthly.ToplamGider);
    }

    // ---------- E: YasAy ay-farkı gün ihmali (sınır) ----------
    [Fact]
    public async Task ProbeE_yas_kovasi_gun_hassas()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var now = DateTimeOffset.UtcNow;

        // Gerçek yaş: 1 yıldan 3 gün EKSİK (AlimTarihi = 1 yıl önce + 3 gün).
        // NOT: ay sonunda (gün>=29) AddDays(3) ay atlatır ve senaryo bozulur → o günlerde atla.
        var at = now.AddYears(-1).AddDays(3);
        if (at.Month != now.Month) return; // run-date guard (probe)
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 YA 01", AlimBedeli = 100m, AlimTarihi = at });
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = v, NetTutar = 10m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit });

        var d = await sp.GetRequiredService<ReportService>().GetFleetAnalysisAsync();
        var row = Assert.Single(d.Satirlar);
        // Düzeltme (F-E): gün-hassas ay farkı — 1 yıldan 3 gün eksik araç hâlâ "0-1 yıl".
        Assert.Equal(11, row.YasAy);
        Assert.Equal("0-1 yıl", Assert.Single(d.YasKohortu).Kova);
    }

    // ---------- E2: gelecekteki AlimTarihi → negatif clamp ----------
    [Fact]
    public async Task ProbeE2_gelecek_alim_tarihi_clamp()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 YA 02", AlimTarihi = DateTimeOffset.UtcNow.AddMonths(5) });
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = v, NetTutar = 10m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit });

        var d = await sp.GetRequiredService<ReportService>().GetFleetAnalysisAsync();
        var row = Assert.Single(d.Satirlar);
        Assert.Equal(0, row.YasAy);   // Math.Max(0, negatif) — "0-1 yıl" kovası
        Assert.Equal("0-1 yıl", Assert.Single(d.YasKohortu).Kova);
    }
}
