using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Baflar;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul bulguları — kiradaki araca "Servise Başla" ve BAF tahsisi reddi, tek açık tahsis (eşzamanlı dahil), servis
/// tamamlanınca araç km'si. BAĞIMSIZ ORACLE: km değerleri elle (giriş 15.000 → çıkış 15.300 ⇒ araç 15.300; araç 20.000
/// iken çıkış 16.000 ⇒ araç 20.000 kalır). Mesajlar Türkçe, alan <c>vehicleId</c>.
/// </summary>
[Collection("postgres")]
public sealed class ServiceAndBafGuardTests(PostgresFixture fx)
{
    private static async Task<(Guid Rental, string ContractNo)> DeliveredRentalAsync(IServiceProvider sp, Guid vehicleId, int km)
    {
        var rentals = sp.GetRequiredService<RentalService>();
        var customer = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Koruma", Soyad = "Test" });
        var rental = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = customer, VehicleId = vehicleId, BasTar = TestZaman.DaysLater(-1), BitTar = TestZaman.DaysLater(2), GunlukUcret = 100m });
        await rentals.DeliverAsync(rental, pickupKm: km, pickupFuel: 8);
        return (rental, (await rentals.GetAsync(rental))!.SozlesmeNo);
    }

    [Fact]
    public async Task Kiradaki_araca_servis_kaydi_acilir_ama_servise_baslanamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var services = sp.GetRequiredService<ServiceRecordService>();
        var id = await veh.CreateAsync(new VehicleInput { Plaka = "34 SRK 01", Km = 2_000 });
        var (rental, contractNo) = await DeliveredRentalAsync(sp, id, 2_000);

        var sid = await services.CreateAsync(new ServiceRecordInput { VehicleId = id, GirisKm = 2_000 }); // kayıt serbest
        var ex = await Assert.ThrowsAsync<ValidationException>(() => services.StartAsync(sid));
        Assert.Equal($"Aracın açık kira sözleşmesi var ({contractNo}); kiradaki araç servise başlatılamaz.", ex.Message);
        Assert.Equal("vehicleId", ex.Alan);
        Assert.Equal(VehicleStatus.Kirada, (await veh.GetAsync(id))!.Durum);
        Assert.Equal(ServiceStatus.Acik, (await services.GetAsync(sid))!.Durum);

        // Dönüşten sonra aynı kayıt başlatılabilir.
        await sp.GetRequiredService<RentalService>().ReturnAsync(rental, returnKm: 2_100, returnFuel: 8, TestZaman.Now());
        Assert.True(await services.StartAsync(sid));
        Assert.Equal(VehicleStatus.Serviste, (await veh.GetAsync(id))!.Durum);
    }

    [Fact]
    public async Task Servis_tamamlaninca_arac_km_ileri_gider_geri_gitmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var services = sp.GetRequiredService<ServiceRecordService>();

        var a = await veh.CreateAsync(new VehicleInput { Plaka = "34 SKM 01", Km = 15_000 });
        var s1 = await services.CreateAsync(new ServiceRecordInput { VehicleId = a, GirisKm = 15_000 });
        await services.StartAsync(s1);
        await services.CompleteAsync(s1, pickupKm: 15_300);
        Assert.Equal(15_300, (await veh.GetAsync(a))!.Km);

        // Araç km'si servis sırasında 20.000'e ilerlemiş; servis çıkış km'si 16.000 → araç km'si GERİ GİTMEZ.
        var b = await veh.CreateAsync(new VehicleInput { Plaka = "34 SKM 02", Km = 15_000 });
        var s2 = await services.CreateAsync(new ServiceRecordInput { VehicleId = b, GirisKm = 15_000 });
        await services.StartAsync(s2);
        await veh.EnterManualKmAsync(b, 20_000);
        await services.CompleteAsync(s2, pickupKm: 16_000);
        Assert.Equal(20_000, (await veh.GetAsync(b))!.Km);
        Assert.Equal(VehicleStatus.Musait, (await veh.GetAsync(b))!.Durum);
    }

    [Fact]
    public async Task Kiradaki_ya_da_acik_tahsisli_araca_tahsis_acilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var bafs = sp.GetRequiredService<BafService>();

        var rented = await veh.CreateAsync(new VehicleInput { Plaka = "34 BFK 01", Km = 3_000 });
        var (_, contractNo) = await DeliveredRentalAsync(sp, rented, 3_000);
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            bafs.CreateAsync(new BafInput { PersonelId = Guid.NewGuid(), VehicleId = rented, CikisKm = 3_000 }));
        Assert.Equal($"Aracın açık kira sözleşmesi var ({contractNo}); kiradaki araca tahsis açılamaz.", ex.Message);
        Assert.Equal("vehicleId", ex.Alan);

        var free = await veh.CreateAsync(new VehicleInput { Plaka = "34 BFK 02", Km = 3_000 });
        var first = await bafs.CreateAsync(new BafInput { PersonelId = Guid.NewGuid(), VehicleId = free, CikisKm = 3_000 });
        var firstNo = (await bafs.GetAsync(first))!.No;
        var second = await Assert.ThrowsAsync<ValidationException>(() =>
            bafs.CreateAsync(new BafInput { PersonelId = Guid.NewGuid(), VehicleId = free, CikisKm = 3_000 }));
        Assert.Equal($"Aracın açık tahsisi var ({firstNo}); önce o tahsisi teslim alın ya da iptal edin.", second.Message);
        Assert.Equal("vehicleId", second.Alan);

        // Aynı işlem anahtarıyla tekrar: tahsis kuralı değil mükerrer (409) — kayıt zaten bu istekle yazıldı.
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            bafs.CreateAsync(new BafInput { PersonelId = Guid.NewGuid(), VehicleId = free, CikisKm = 3_000, IslemAnahtari = first }));

        // Teslim alındıktan sonra yeni tahsis açılabilir.
        Assert.True(await bafs.ReceiveAsync(first, returnKm: 3_200, returnFuel: 6));
        var again = await bafs.CreateAsync(new BafInput { PersonelId = Guid.NewGuid(), VehicleId = free, CikisKm = 3_200 });
        Assert.Equal(BafStatus.Acik, (await bafs.GetAsync(again))!.Durum);
    }

    [Fact]
    public async Task Eszamanli_iki_tahsisten_yalniz_biri_yazilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid vehicle;
        using (var s = host.ScopeFor(tenant))
            vehicle = await s.ServiceProvider.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = "34 BFE 01", Km = 1_000 });

        for (var round = 0; round < 3; round++)
        {
            using var s1 = host.ScopeFor(tenant);
            using var s2 = host.ScopeFor(tenant);
            var results = await Task.WhenAll(
                Try(s1.ServiceProvider.GetRequiredService<BafService>(), vehicle),
                Try(s2.ServiceProvider.GetRequiredService<BafService>(), vehicle));
            Assert.Equal(1, results.Count(r => r is not null));
            using var s3 = host.ScopeFor(tenant);
            var b = s3.ServiceProvider.GetRequiredService<BafService>();
            var open = (await b.ListAsync()).Where(x => x.VehicleId == vehicle && x.Durum == BafStatus.Acik).ToList();
            var single = Assert.Single(open);
            Assert.True(await b.ReceiveAsync(single.Id, returnKm: 1_000, returnFuel: null));
        }

        static async Task<Guid?> Try(BafService svc, Guid vehicleId)
        {
            try { return await svc.CreateAsync(new BafInput { PersonelId = Guid.NewGuid(), VehicleId = vehicleId, CikisKm = 1_000 }); }
            catch (ValidationException ex) when (ex.Alan == "vehicleId") { return null; }
        }
    }
}
