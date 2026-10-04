using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Baflar;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul bulguları (araç durumu korumaları) — araç kartı km/durum alanları. BAĞIMSIZ ORACLE: beklenen değerler elle
/// kurulan senaryodan (km 15.500 → 9.000 red, 16.000 kabul; kiradaki araç "Müsait" yapılamaz, dönüşten sonra Pasif
/// yapılabilir). Hata mesajları Türkçe ve alana bağlı (<see cref="ValidationException.Alan"/>).
/// </summary>
[Collection("postgres")]
public sealed class VehicleStateGuardTests(PostgresFixture fx)
{
    private static VehicleInput Card(Vehicle v, VehicleStatus? status = null, int? km = null, string? brand = null) => new()
    {
        Plaka = v.Plaka, Marka = brand ?? v.Marka, Grup = v.Grup, GrupBilincliBos = v.Grup is null,
        Durum = status ?? v.Durum, Km = km ?? v.Km,
    };

    [Fact]
    public async Task Kart_km_geri_alinamaz_ileri_ve_ayni_km_kabul()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var veh = scope.ServiceProvider.GetRequiredService<VehicleService>();
        var id = await veh.CreateAsync(new VehicleInput { Plaka = "34 KMG 01", Marka = "Fiat", Km = 15_500 });
        var v = (await veh.GetAsync(id))!;

        // Reddedilen: 15.500 → 9.000 (kart). Odometre değişmez.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => veh.UpdateAsync(id, Card(v, km: 9_000)));
        Assert.Equal("KM geriye gidemez (araç odometresi 15500).", ex.Message);
        Assert.Equal("km", ex.Alan);
        Assert.Equal(15_500, (await veh.GetAsync(id))!.Km);

        // İzin verilen: aynı km ile başka alan değişir; ileri km (16.000) yazılır.
        Assert.True(await veh.UpdateAsync(id, Card(v, brand: "Renault")));
        Assert.True(await veh.UpdateAsync(id, Card(v, km: 16_000)));
        Assert.Equal(16_000, (await veh.GetAsync(id))!.Km);

        // Tek kural: manuel km girişi de AYNI mesajla ve AYNI alanla reddeder.
        var manual = await Assert.ThrowsAsync<ValidationException>(() => veh.EnterManualKmAsync(id, 15_000));
        Assert.Equal("KM geriye gidemez (araç odometresi 16000).", manual.Message);
        Assert.Equal("km", manual.Alan);
    }

    [Fact]
    public async Task Kiradaki_arac_kartta_musait_ya_da_pasif_yapilamaz_donusten_sonra_yapilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await veh.CreateAsync(new VehicleInput { Plaka = "34 KDR 01", Marka = "Fiat", Km = 1_000 });
        var customer = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Durum", Soyad = "Test" });
        var rental = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = customer, VehicleId = id, BasTar = TestZaman.DaysLater(-1), BitTar = TestZaman.DaysLater(2), GunlukUcret = 100m });
        await rentals.DeliverAsync(rental, pickupKm: 1_000, pickupFuel: 8);
        var v = (await veh.GetAsync(id))!;
        Assert.Equal(VehicleStatus.Kirada, v.Durum);
        var contractNo = (await rentals.GetAsync(rental))!.SozlesmeNo;

        // Reddedilen: Kirada → Müsait ve Kirada → Pasif (açık kira var). Durum değişmez.
        foreach (var target in new[] { VehicleStatus.Musait, VehicleStatus.Pasif })
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => veh.UpdateAsync(id, Card(v, target)));
            Assert.Equal($"Aracın açık kira sözleşmesi var ({contractNo}); durumu kira dönüşü ya da iptaliyle değişir.", ex.Message);
            Assert.Equal("durum", ex.Alan);
        }
        Assert.Equal(VehicleStatus.Kirada, (await veh.GetAsync(id))!.Durum);

        // İzin verilen: durum AYNI kalırken kartın başka alanı düzenlenebilir.
        Assert.True(await veh.UpdateAsync(id, Card(v, brand: "Renault")));

        // Dönüşten sonra (araç Müsait, açık kira yok): Pasif → Müsait serbest; Kirada/Serviste elle hiçbir zaman.
        await rentals.ReturnAsync(rental, returnKm: 1_200, returnFuel: 8, TestZaman.Now());
        v = (await veh.GetAsync(id))!;
        Assert.Equal(VehicleStatus.Musait, v.Durum);
        Assert.True(await veh.UpdateAsync(id, Card(v, VehicleStatus.Pasif)));
        Assert.Equal(VehicleStatus.Pasif, (await veh.GetAsync(id))!.Durum);
        Assert.True(await veh.UpdateAsync(id, Card(v, VehicleStatus.Musait)));
        var toRented = await Assert.ThrowsAsync<ValidationException>(() => veh.UpdateAsync(id, Card(v, VehicleStatus.Kirada)));
        Assert.Equal("Araç durumu elle 'Kirada' yapılamaz; kira sözleşmesinde teslimle değişir.", toRented.Message);
        Assert.Equal("durum", toRented.Alan);
        var toService = await Assert.ThrowsAsync<ValidationException>(() => veh.UpdateAsync(id, Card(v, VehicleStatus.Serviste)));
        Assert.Equal("Araç durumu elle 'Serviste' yapılamaz; servis kaydında 'Servise Başla' ile değişir.", toService.Message);
        Assert.Equal("durum", toService.Alan);
    }

    [Fact]
    public async Task Servisteki_ve_tahsisli_arac_kartta_durum_degistiremez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var services = sp.GetRequiredService<RentACar.Application.ServiceRecords.ServiceRecordService>();
        var bafs = sp.GetRequiredService<BafService>();
        var id = await veh.CreateAsync(new VehicleInput { Plaka = "34 SRD 01", Marka = "Fiat", Km = 5_000 });

        var sid = await services.CreateAsync(new RentACar.Application.ServiceRecords.ServiceRecordInput { VehicleId = id, GirisKm = 5_000 });
        await services.StartAsync(sid);
        var v = (await veh.GetAsync(id))!;
        Assert.Equal(VehicleStatus.Serviste, v.Durum);
        var serviceNo = (await services.GetAsync(sid))!.No;
        var ex = await Assert.ThrowsAsync<ValidationException>(() => veh.UpdateAsync(id, Card(v, VehicleStatus.Musait)));
        Assert.Equal($"Araç serviste ({serviceNo}); durumu servis tamamlanınca ya da iptal edilince değişir.", ex.Message);
        Assert.Equal("durum", ex.Alan);
        await services.CompleteAsync(sid, pickupKm: 5_100);

        // Açık tahsis (BAF) varken Pasif yapılamaz; teslim alındıktan sonra yapılabilir.
        var baf = await bafs.CreateAsync(new BafInput { PersonelId = Guid.NewGuid(), VehicleId = id, CikisKm = 5_100 });
        v = (await veh.GetAsync(id))!;
        var bafNo = (await bafs.GetAsync(baf))!.No;
        var bafEx = await Assert.ThrowsAsync<ValidationException>(() => veh.UpdateAsync(id, Card(v, VehicleStatus.Pasif)));
        Assert.Equal($"Aracın açık tahsisi var ({bafNo}); önce tahsisi teslim alın ya da iptal edin.", bafEx.Message);
        Assert.Equal("durum", bafEx.Alan);
        Assert.True(await bafs.ReceiveAsync(baf, returnKm: 5_300, returnFuel: 6));
        Assert.True(await veh.UpdateAsync(id, Card(v, VehicleStatus.Pasif)));
        Assert.Equal(VehicleStatus.Pasif, (await veh.GetAsync(id))!.Durum);
    }

    [Fact]
    public async Task Satis_kaydi_olan_arac_satildi_durumundan_cikamaz_kayitsiz_satildi_geri_alinir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();

        // Kayıtsız elle "Satıldı" (filo dışı satış işaretlemesi) geri alınabilir.
        var manual = await veh.CreateAsync(new VehicleInput { Plaka = "34 STL 01", Marka = "Fiat" });
        var m = (await veh.GetAsync(manual))!;
        Assert.True(await veh.UpdateAsync(manual, Card(m, VehicleStatus.Satildi)));
        Assert.True(await veh.UpdateAsync(manual, Card(m, VehicleStatus.Musait)));

        // Tamamlanmış satış kaydı olan araç kartta "Satıldı"dan çıkarılamaz (satış defterle çelişirdi).
        var sold = await veh.CreateAsync(new VehicleInput { Plaka = "34 STL 02", Marka = "Fiat", Durum = VehicleStatus.Satildi });
        var saleNo = "ST-" + Guid.NewGuid().ToString("N")[..6];
        var dbf = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await dbf.CreateDbContextAsync())
        {
            db.VehicleSales.Add(new VehicleSale
            { No = saleNo, VehicleId = sold, AliciCariId = Guid.NewGuid(), SatisNet = 100m, GenelToplam = 100m });
            await db.SaveChangesAsync();
        }
        var s = (await veh.GetAsync(sold))!;
        var ex = await Assert.ThrowsAsync<ValidationException>(() => veh.UpdateAsync(sold, Card(s, VehicleStatus.Musait)));
        Assert.Equal($"Araç satış kaydıyla satılmış ({saleNo}); durumu kart üzerinden değiştirilemez.", ex.Message);
        Assert.Equal("durum", ex.Alan);
        Assert.Equal(VehicleStatus.Satildi, (await veh.GetAsync(sold))!.Durum);
    }
}
