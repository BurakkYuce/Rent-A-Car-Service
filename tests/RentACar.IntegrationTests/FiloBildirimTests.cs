using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Notifications;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ 6.2 — filo bildirimleri (bakım-km ≤1000 kalan + tut/sat ≥2 sinyal) ve aylık gelir trendi.
/// BAĞIMSIZ ORACLE: kalan-km/oran/pencere değerleri elle senaryodan. Üretici FiloAnaliz/rapor
/// sayfasıyla AYNI kaynakları kullanır (OrtakSorgular); idempotens (Tur,Araç,anahtar) ile.
/// </summary>
[Collection("postgres")]
public sealed class FiloBildirimTests(PostgresFixture fx)
{
    private static async Task<int> UretAsync(IServiceProvider sp, Guid tenant, DateTimeOffset now)
    {
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await FiloBildirimUretici.RunAsync(db, tenant, now, TutSatEsikleri.Default);
    }

    /// <summary>Servis kaydı aç → başlat → tamamla (sonraki bakım hedefi yaz).</summary>
    private static async Task ServisHedefAsync(IServiceProvider sp, Guid veh, int girisKm, int cikisKm, int hedef)
    {
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var id = await svc.CreateAsync(new ServiceRecordInput { VehicleId = veh, GirisKm = girisKm });
        Assert.True(await svc.StartAsync(id));
        Assert.True(await svc.CompleteAsync(id, cikisKm, nextMaintenanceKm: hedef));
    }

    [Fact]
    public async Task Bakim_km_esigi_bildirim_uretir_idempotent_yeni_hedef_yeni_dongu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var now = DateTimeOffset.UtcNow;

        // V1: Km 9.500, hedef 10.000 → kalan 500 ≤ 1000 → bildirim ("yaklaşıyor").
        // V2: Km 1.000, hedef 20.000 → kalan 19.000 → YOK.
        // V3: Km 10.500, hedef 10.000 → kalan −500 → bildirim ("AŞILDI").
        var v1 = await veh.CreateAsync(new VehicleInput { Plaka = "34 BK 01", Km = 9_500 });
        var v2 = await veh.CreateAsync(new VehicleInput { Plaka = "34 BK 02", Km = 1_000 });
        var v3 = await veh.CreateAsync(new VehicleInput { Plaka = "34 BK 03", Km = 10_500 });
        await ServisHedefAsync(sp, v1, 9_000, 9_500, 10_000);
        await ServisHedefAsync(sp, v2, 900, 1_000, 20_000);
        await ServisHedefAsync(sp, v3, 10_000, 10_500, 10_000);

        Assert.Equal(2, await UretAsync(sp, tenant, now));            // V1 + V3 (elle)
        Assert.Equal(0, await UretAsync(sp, tenant, now));            // idempotent

        var bildirimler = await sp.GetRequiredService<InAppNotificationService>().ListPersistedAsync();
        var bakimlar = bildirimler.Where(b => b.Tur == "Bakım-Km").ToList();
        Assert.Equal(2, bakimlar.Count);
        Assert.Contains(bakimlar, b => b.VehicleId == v1 && b.Mesaj.Contains("kalan 500"));
        Assert.Contains(bakimlar, b => b.VehicleId == v3 && b.Mesaj.Contains("AŞILDI"));

        // Yeni bakım DÖNGÜSÜ: V3'e yeni hedef 11.000 (MAX birleşimi) → kalan 500 → FARKLI anahtar → 1 yeni.
        await ServisHedefAsync(sp, v3, 10_500, 10_500, 11_000);
        Assert.Equal(1, await UretAsync(sp, tenant, now));
        Assert.Equal(0, await UretAsync(sp, tenant, now));
    }

    [Fact]
    public async Task Tut_sat_iki_sinyalde_bildirim_uretir_ay_cipasiyla_idempotent()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var simdi = DateTimeOffset.UtcNow;

        // V1 (TutSatSinyaliTests oracle'ı): İkinciEl 1000, son-12 gider 500 → oran 0.50 > 0.45 (kural-a);
        // km-maliyet önceki 150/300=0.50 → son 500/250=2.00 (kural-c). Tek-araç sınıf → kural-b YOK → sinyal 2.
        var v1 = await veh.CreateAsync(new VehicleInput { Plaka = "34 FB 01", Grup = "EKO", IkinciElDeger = 1000m });
        var exp = sp.GetRequiredService<ExpenseService>();
        await exp.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = v1, NetTutar = 500m, KdvOrani = 0m, Tarih = simdi.AddMonths(-2), OdemeYontemi = PaymentMethod.Nakit });
        await exp.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = v1, NetTutar = 150m, KdvOrani = 0m, Tarih = simdi.AddMonths(-18), OdemeYontemi = PaymentMethod.Nakit });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "FB", Soyad = "C" });
        var rentals = sp.GetRequiredService<RentalService>();
        async Task KiraKmAsync(DateTimeOffset bas, int km)
        {
            var r = await rentals.CreateDirectAsync(new BookingInput
            { MusteriId = cari, VehicleId = v1, BasTar = bas, BitTar = bas.AddDays(2), GunlukUcret = 10m });
            await rentals.DeliverAsync(r, pickupKm: 0, pickupFuel: 8);
            await rentals.ReturnAsync(r, returnKm: km, returnFuel: 8, bas.AddDays(2));
        }
        await KiraKmAsync(simdi.AddMonths(-18), 300);                 // önceki-12 penceresi
        await KiraKmAsync(simdi.AddMonths(-2), 250);                  // son-12 penceresi

        // V2: sinyalsiz temiz araç (İkinciEl yok → a/b yok; km penceresi boş → c yok).
        await veh.CreateAsync(new VehicleInput { Plaka = "34 FB 02", Grup = "EKO" });

        Assert.Equal(1, await UretAsync(sp, tenant, simdi));          // yalnız V1
        Assert.Equal(0, await UretAsync(sp, tenant, simdi));          // ay çıpası → idempotent

        var b = Assert.Single((await sp.GetRequiredService<InAppNotificationService>().ListPersistedAsync())
            .Where(x => x.Tur == "Tut/Sat"));
        Assert.Equal(v1, b.VehicleId);
        Assert.Contains("tut/sat sinyali (2/3)", b.Mesaj);
    }

    [Fact]
    public async Task Aylik_gelir_trendi_ay_pencereleriyle_kirilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var simdi = DateTimeOffset.UtcNow;
        var buAy = new DateTimeOffset(simdi.Year, simdi.Month, 1, 0, 0, 0, TimeSpan.Zero);

        // Manuel fatura (KDV %0 → gelir = net, elle): geçen ayın 15'i 100; bu ayın 1'i 300.
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Trend", Soyad = "C" });
        var inv = sp.GetRequiredService<InvoiceService>();
        await inv.CreateManualAsync(new ManualInvoiceInput
        { CariId = cari, NetTutar = 100m, KdvOrani = 0m, Tarih = buAy.AddMonths(-1).AddDays(14), Aciklama = "gecen ay" });
        await inv.CreateManualAsync(new ManualInvoiceInput
        { CariId = cari, NetTutar = 300m, KdvOrani = 0m, Tarih = buAy, Aciklama = "bu ay" });

        var trend = await sp.GetRequiredService<ReportService>().GetMonthlyRevenueTrendAsync(3, simdi);
        Assert.Equal(3, trend.Count);
        Assert.Equal(0m, trend[0].Gelir);                             // 2 ay önce: boş
        Assert.Equal(100m, trend[1].Gelir);                           // geçen ay (elle)
        Assert.Equal(300m, trend[2].Gelir);                           // bu ay (ay-başı kaydı bu aya, geçene DEĞİL)
        Assert.Equal(buAy, trend[2].AyBas);
    }
}
