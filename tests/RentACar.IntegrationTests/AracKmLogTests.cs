using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ2-2.5 — Araç km zaman serisi (VehicleKmLog). Otomatik yazım kaynağıyla AYNI transaction'da:
/// kira dönüşü (DonusKm) + servis tamamlama (CikisKm); manuel giriş araç kartından (Vehicle.Km de
/// güncellenir; GERİYE GİTME REDDİ). BAĞIMSIZ ORACLE (elle): dönüş 1000→1300 → seri [1300 Donus];
/// manuel 1350 → [1350 Manuel, 1300 Donus]; geriye 1200 → red + seri değişmez; karne DÖNEM KM
/// (from=T-7): pencere-içi son 1350 − pencere-öncesi son 1300 = 50; dönem gideri 100 → km-maliyet
/// 100/50 = 2.00. Tenant izolasyonu: yabancı tenant seriyi göremez (RLS + filtre).
/// </summary>
[Collection("postgres")]
public sealed class AracKmLogTests(PostgresFixture fx)
{
    [Fact]
    public async Task Donus_manuel_geriye_red_ve_donem_farki()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var simdi = DateTimeOffset.UtcNow;

        var v = await veh.CreateAsync(new VehicleInput { Plaka = "34 KM 01" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Km", Soyad = "C" });

        // Kira: teslim 1000 → dönüş 1300 (gerçek dönüş T-10). Dönüş logu AYNI transaction'da düşer.
        var rentals = sp.GetRequiredService<RentalService>();
        var r = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = v, BasTar = simdi.AddDays(-12), BitTar = simdi.AddDays(-10), GunlukUcret = 100m });
        await rentals.DeliverAsync(r, cikisKm: 1000, cikisYakit: 8);
        await rentals.ReturnAsync(r, donusKm: 1300, donusYakit: 8, simdi.AddDays(-10));

        var seri1 = await veh.KmLoglariAsync(v);
        var donus = Assert.Single(seri1);
        Assert.Equal(1300, donus.Km);
        Assert.Equal(KmLogKaynak.Donus, donus.Kaynak);
        Assert.Equal(simdi.AddDays(-10), donus.Tarih);   // log tarihi = gerçek dönüş tarihi

        // Manuel 1350: log + Vehicle.Km birlikte.
        await veh.ManuelKmGirAsync(v, 1350);
        Assert.Equal(1350, (await veh.GetAsync(v))!.Km);
        var seri2 = await veh.KmLoglariAsync(v);
        Assert.Equal(2, seri2.Count);
        Assert.Equal(1350, seri2[0].Km);                 // en yeni önce
        Assert.Equal(KmLogKaynak.Manuel, seri2[0].Kaynak);

        // Geriye 1200: red — seri ve odometre DEĞİŞMEZ (odometre monoton).
        await Assert.ThrowsAsync<ValidationException>(() => veh.ManuelKmGirAsync(v, 1200));
        Assert.Equal(2, (await veh.KmLoglariAsync(v)).Count);
        Assert.Equal(1350, (await veh.GetAsync(v))!.Km);

        // Dönem km (karne, from=T-7): son log 1350 (bugün) − pencere-öncesi son 1300 (T-10) = 50 (elle).
        // Dönem gideri 100 (T-3) → dönem km-maliyet 100/50 = 2.00 (elle). KPI ömür tanımı ayrı kalır.
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Arac, VehicleId = v, NetTutar = 100m, KdvOrani = 0m,
            Tarih = simdi.AddDays(-3), OdemeYontemi = OdemeYontemi.Nakit
        });
        var karne = (await sp.GetRequiredService<ReportService>()
            .GetAracKarneAsync(v, from: simdi.AddDays(-7)))!;
        Assert.Equal(50, karne.DonemKm);
        Assert.Equal(2.00m, karne.DonemKmMaliyet);
    }

    [Fact]
    public async Task Servis_tamamlama_cikis_km_loglar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();

        var v = await veh.CreateAsync(new VehicleInput { Plaka = "34 KM 02" });
        var servis = sp.GetRequiredService<ServiceRecordService>();
        var sid = await servis.CreateAsync(new ServiceRecordInput { VehicleId = v, GirisKm = 500 });
        await servis.BaslatAsync(sid);
        await servis.TamamlaAsync(sid, cikisKm: 800);

        var log = Assert.Single(await veh.KmLoglariAsync(v));
        Assert.Equal(800, log.Km);
        Assert.Equal(KmLogKaynak.Servis, log.Kaynak);
    }

    [Fact]
    public async Task Tenant_izolasyonu_seri_sizmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = Guid.NewGuid();
        Guid vehId;
        using (var scopeA = host.ScopeFor(tenantA))
        {
            var vehA = scopeA.ServiceProvider.GetRequiredService<VehicleService>();
            vehId = await vehA.CreateAsync(new VehicleInput { Plaka = "34 KM 03" });
            await vehA.ManuelKmGirAsync(vehId, 100);
            Assert.Single(await vehA.KmLoglariAsync(vehId));
        }
        using var scopeB = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await scopeB.ServiceProvider.GetRequiredService<VehicleService>().KmLoglariAsync(vehId));
    }
}
