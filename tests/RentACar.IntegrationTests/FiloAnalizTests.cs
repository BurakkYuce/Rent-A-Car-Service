using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Araç-karne PR5 — Filo Analiz Panosu (GetFiloAnalizAsync). BAĞIMSIZ ORACLE (elle):
/// V1 rücu 620×0.5=310 net + kira 3/31 gün → doluluk 9,68, ROI 310/1000=31,00;
/// V2 rücu 100 − gider 150 = −50 (en zararlı, alımsız → ROI/yaş null);
/// V3 rücu 500 (en kârlı). Toplam 910/150/760 = defter (invaryant). Yaş kovaları now-göreli
/// AlimTarihi ile deterministik (tarih-politikası dersi: sabit tarih değil, now-göreli).
/// </summary>
[Collection("postgres")]
public sealed class FiloAnalizTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset FleetEntry = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FleetExit = new(2025, 1, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RentalStart = new(2025, 1, 10, 9, 0, 0, TimeSpan.Zero);

    private static async Task RecourseAsync(IServiceProvider sp, Guid vehicle, Guid account, decimal cost)
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
        await svc.ReflectAsync(id, account);
    }

    [Fact]
    public async Task Siralama_toplam_kpi_ve_kohort_elle_oracle()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Filo", Soyad = "Cari" });
        var now = DateTimeOffset.UtcNow;

        // V1: 1000 alım (6 ay önce → "0-1 yıl"), Oca-2025 penceresi (31 gün), 3 gün kira + 300 km, rücu 310.
        var v1 = await veh.CreateAsync(new VehicleInput
        {
            Plaka = "34 FA 01", AlimBedeli = 1000m, AlimTarihi = now.AddMonths(-6),
            FiloGirisTarih = FleetEntry, FiloCikisTarih = FleetExit
        });
        var rentals = sp.GetRequiredService<RentalService>();
        var r1 = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = account, VehicleId = v1, BasTar = RentalStart, BitTar = RentalStart.AddDays(2), GunlukUcret = 100m });
        await rentals.DeliverAsync(r1, pickupKm: 1000, pickupFuel: 8);
        await rentals.ReturnAsync(r1, returnKm: 1300, returnFuel: 8, RentalStart.AddDays(2));
        await RecourseAsync(sp, v1, account, 620m);   // 310 gelir

        // V2: alımsız; rücu 100 − gider 150 = NET −50 (en zararlı).
        var v2 = await veh.CreateAsync(new VehicleInput { Plaka = "34 FA 02" });
        await RecourseAsync(sp, v2, account, 200m);   // 100 gelir
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = v2, NetTutar = 150m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit });

        // V3: alım tarihi 15 ay önce ("1-2 yıl"); rücu 500 (en kârlı).
        var v3 = await veh.CreateAsync(new VehicleInput { Plaka = "34 FA 03", AlimTarihi = now.AddMonths(-15) });
        await RecourseAsync(sp, v3, account, 1000m);  // 500 gelir

        var rs = sp.GetRequiredService<ReportService>();
        var d = await rs.GetFleetAnalysisAsync();

        // Varsayılan sıralama: en kârlı önce (elle: 500 > 310 > −50).
        Assert.Equal(3, d.Satirlar.Count);
        Assert.Equal(new[] { v3, v1, v2 }, d.Satirlar.Select(x => x.VehicleId).ToArray());
        Assert.Equal(500m, d.Satirlar[0].NetKar);
        Assert.Equal(310m, d.Satirlar[1].NetKar);
        Assert.Equal(-50m, d.Satirlar[2].NetKar);

        // V1 KPI (elle): doluluk 3×100/31 = 9,68; ROI 310×100/1000 = 31,00; km-maliyet 0/300 = 0,00.
        var s1 = d.Satirlar.Single(x => x.VehicleId == v1);
        Assert.Equal(31, s1.SahiplikGun);
        Assert.Equal(3, s1.KiralananGun);
        Assert.Equal(9.68m, s1.DolulukYuzde);
        Assert.Equal(31.00m, s1.RoiYuzde);
        Assert.Equal(0.00m, s1.KmBasinaMaliyet);

        // V2: alımsız → ROI/yaş null; penceresiz → doluluk null.
        var s2 = d.Satirlar.Single(x => x.VehicleId == v2);
        Assert.Null(s2.RoiYuzde);
        Assert.Null(s2.YasAy);
        Assert.Null(s2.DolulukYuzde);

        // Toplamlar defterle MUTABIK (invaryant): 910 / 150 / 760; Atanmamış 0.
        Assert.Equal(910m, d.ToplamGelir);
        Assert.Equal(150m, d.ToplamGider);
        Assert.Equal(760m, d.ToplamNetKar);
        Assert.Equal(0m, d.AtanmamisGelir);
        var gg = await rs.GetRevenueExpenseAsync();
        Assert.Equal(gg.GelirToplam, d.ToplamGelir);
        Assert.Equal(gg.GiderToplam, d.ToplamGider);

        // Kohort: 0-1 yıl (V1), 1-2 yıl (V3), Alım tarihi yok (V2) — bu sırayla.
        Assert.Equal(new[] { "0-1 yıl", "1-2 yıl", "Alım tarihi yok" }, d.YasKohortu.Select(k => k.Kova).ToArray());
        Assert.All(d.YasKohortu, k => Assert.Equal(1, k.AracAdet));
        Assert.Equal(0.00m, d.YasKohortu[0].OrtKmMaliyet);   // V1 kovası
        Assert.Equal(9.68m, d.YasKohortu[0].OrtDoluluk);

        // "zarar" sıralaması: en zararlı önce.
        var loss = await rs.GetFleetAnalysisAsync(sort: "zarar");
        Assert.Equal(v2, loss.Satirlar[0].VehicleId);
    }

    [Fact]
    public async Task Atanmamis_ayri_gosterilir_ve_toplam_mutabik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 FA 04" });
        var exp = sp.GetRequiredService<ExpenseService>();
        // Araç gideri 100 (satıra) + genel gider 40 (Atanmamış'a — satır DEĞİL, ayrı gösterim).
        await exp.CreateAsync(new ExpenseInput { Tip = ExpenseType.Arac, VehicleId = v, NetTutar = 100m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit });
        await exp.CreateAsync(new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 40m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit });

        var d = await sp.GetRequiredService<ReportService>().GetFleetAnalysisAsync();
        var row = Assert.Single(d.Satirlar);
        Assert.Equal(100m, row.Gider);
        Assert.Equal(40m, d.AtanmamisGider);
        Assert.Equal(140m, d.ToplamGider);   // satır + Atanmamış = defter
    }

    [Fact]
    public async Task Capraz_tenant_bos()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = Guid.NewGuid();
        using (var scopeA = host.ScopeFor(tenantA))
        {
            var sp = scopeA.ServiceProvider;
            var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 FA 05" });
            await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
            { Tip = ExpenseType.Arac, VehicleId = v, NetTutar = 99m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit });
        }
        using var scopeB = host.ScopeFor(Guid.NewGuid());
        var d = await scopeB.ServiceProvider.GetRequiredService<ReportService>().GetFleetAnalysisAsync();
        Assert.Empty(d.Satirlar);
        Assert.Equal(0m, d.ToplamGider);
    }
}
