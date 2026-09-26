using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Reporting;
using RentACar.Application.ServisTanimlari;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR1 — dönüş/teslimde araç odometresi (Vehicle.Km) + km-bazlı bakım-due panosu. BAĞIMSIZ ORACLE:
/// beklenen km/kalan değerleri elle (50.000; 40.000+15.000−52.000=3.000 vb.), servis kodundan türetilmez.
/// Kira geçmişe açık → geçmiş tarih kullanılabilir; tarih tabanı whole-second (PG µs dersi).
/// </summary>
[Collection("postgres")]
public sealed class DonusAracKmTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-10).AddHours(9);

    private static async Task<(Guid veh, Guid rental)> RentalAsync(
        IServiceProvider sp, string plate, int vehicleKm = 0, string? tip = null)
    {
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Km", Soyad = "Musteri" });
        var veh = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate, Km = vehicleKm, Tip = tip });
        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = account, VehicleId = veh, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 100m });
        return (veh, rental);
    }

    private static Task<RentACar.Domain.Entities.Vehicle?> VehicleAsync(IServiceProvider sp, Guid id)
        => sp.GetRequiredService<VehicleService>().GetAsync(id);

    // ---------- Odometre güncelleme ----------
    [Fact]
    public async Task Teslim_ve_donus_arac_odometresini_gunceller()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var (veh, rental) = await RentalAsync(sp, "34 KM 10", vehicleKm: 42000);

        await rentals.DeliverAsync(rental, pickupKm: 45000, pickupFuel: 8);
        Assert.Equal(45000, (await VehicleAsync(sp, veh))!.Km); // çıkışta da güncellenir

        await rentals.ReturnAsync(rental, returnKm: 50000, returnFuel: 8, Start.AddDays(3));
        Assert.Equal(50000, (await VehicleAsync(sp, veh))!.Km); // dönüşte odometre = dönüş km
    }

    [Fact]
    public async Task Kucuk_km_araci_geri_sarmaz_monoton()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        // Araç kartında 60.000 yazıyor; kira çıkış/dönüş km'si daha küçük girildi (eski veri) →
        // KİRA kaydolur ama araç odometresi GERİ SARILMAZ (monoton).
        var (veh, rental) = await RentalAsync(sp, "34 KM 11", vehicleKm: 60000);
        await rentals.DeliverAsync(rental, pickupKm: 45000, pickupFuel: 8);
        await rentals.ReturnAsync(rental, returnKm: 50000, returnFuel: 8, Start.AddDays(3));

        var vehicle = await VehicleAsync(sp, veh);
        Assert.Equal(60000, vehicle!.Km); // değişmedi
        var rentalItem = await rentals.GetAsync(rental);
        Assert.Equal(50000, rentalItem!.DonusKm); // kira kaydı yine tam
        Assert.Equal(RentalStatus.Tamamlandi, rentalItem.Durum);
    }

    [Fact]
    public async Task Gercekci_olmayan_km_farki_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var (_, rental) = await RentalAsync(sp, "34 KM 12");
        await rentals.DeliverAsync(rental, pickupKm: 10000, pickupFuel: 8);
        // Parmak hatası: 10.000 → 500.000 (fark 490.000 > 100.000) — dönüş geri alınamaz, erken red.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => rentals.ReturnAsync(rental, returnKm: 500000, returnFuel: 8, Start.AddDays(3)));
        Assert.Contains("gerçekçi değil", ex.Message);
    }

    // ---------- Km-bazlı bakım-due (iki kaynak + dedup + tanım-yok) ----------
    [Fact]
    public async Task Bakim_due_otomatik_kaynak_sontabakim_arti_aralik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // ServisTanim: Sedan → 15.000 km aralık. Araç: Tip=Sedan, SonBakimKm=40.000, Km=52.000.
        await sp.GetRequiredService<ServiceDefinitionService>().CreateAsync(new ServisTanimInput
        { Kod = "PB-SEDAN", AracTipi = "Sedan", BakimKm = 15000, Aktif = true });
        await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 BK 01", Tip = "sedan", Km = 52000, SonBakimKm = 40000 }); // case-insensitive eşleşme

        var rows = await sp.GetRequiredService<ReportService>().GetPeriodicServiceAsync();
        var r = Assert.Single(rows, x => x.Plaka == "34BK01");
        Assert.Equal(55000, r.SonrakiBakimKm); // 40.000 + 15.000 (elle oracle)
        Assert.Equal(3000, r.KalanKm);         // 55.000 − 52.000
        Assert.Equal("Tanım", r.Kaynak);
    }

    [Fact]
    public async Task Bakim_due_iki_kaynak_tek_satir_min_kalan()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ServiceDefinitionService>().CreateAsync(new ServisTanimInput
        { Kod = "PB-SUV", AracTipi = "SUV", BakimKm = 20000, Aktif = true });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 BK 02", Tip = "SUV", Km = 50000, SonBakimKm = 45000, Durum = VehicleStatus.Musait });
        // Servis kaydı kaynağı: elle hedef 52.000 (otomatik 45.000+20.000=65.000'den DAHA ERKEN).
        var service = sp.GetRequiredService<RentACar.Application.ServiceRecords.ServiceRecordService>();
        var sId = await service.CreateAsync(new RentACar.Application.ServiceRecords.ServiceRecordInput
        { VehicleId = veh, GirisKm = 50000, Aciklama = "periyodik" });
        await service.StartAsync(sId); // Açık → Serviste (durum akışı)
        await service.CompleteAsync(sId, pickupKm: 50000, nextMaintenanceKm: 52000);

        var rows = await sp.GetRequiredService<ReportService>().GetPeriodicServiceAsync();
        var r = Assert.Single(rows, x => x.Plaka == "34BK02"); // TEK satır (dedup)
        Assert.Equal(52000, r.SonrakiBakimKm); // MIN(kalan): 2.000 < 15.000
        Assert.Equal(2000, r.KalanKm);
        Assert.Equal("Servis", r.Kaynak);
    }

    [Fact]
    public async Task Bakim_due_tanimsiz_arac_gizlenmez_tanim_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 BK 03", Km = 10000 }); // tip yok, SonBakimKm yok, servis kaydı yok

        var rows = await sp.GetRequiredService<ReportService>().GetPeriodicServiceAsync();
        var r = Assert.Single(rows, x => x.Plaka == "34BK03");
        Assert.Null(r.SonrakiBakimKm); // "tanım yok" satırı — sessiz gizleme YOK
        Assert.Null(r.KalanKm);
        Assert.Null(r.Kaynak);
    }
}
