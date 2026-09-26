using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Availability;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Araç durum yaşam döngüsü: yeni araç Musait (boşta); teslim→Kirada; dönüş→Musait; iptal→Musait.
/// (Kafa karıştıran operasyonel "Stokta" değeri kaldırıldı — boşta=Musait tek kavram.) Müsaitlik havuzu Kirada'yı
/// DAHİL eder (ileri-tarih bookingi bozulmaz — tarih-çakışması gerçek dışlamayı yapar).
/// </summary>
[Collection("postgres")]
public sealed class AracDurumYasamDongusuTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3).AddHours(9);

    private static async Task<(Guid m, Guid v)> SeedAsync(IServiceProvider sp, string plaka)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "AD", Soyad = "M" });
        return (m, v);
    }

    [Fact]
    public async Task Yeni_arac_musait_bosta()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (_, v) = await SeedAsync(sp, "34 DR 01");
        var arac = await sp.GetRequiredService<VehicleService>().GetAsync(v);
        Assert.Equal(VehicleStatus.Musait, arac!.Durum); // Stokta DEĞİL — boşta görünür
    }

    [Fact]
    public async Task Teslim_kirada_donus_musait()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var vehicles = sp.GetRequiredService<VehicleService>();
        var (m, v) = await SeedAsync(sp, "34 DR 02");
        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m });

        await rentals.DeliverAsync(id, pickupKm: 1000, pickupFuel: 8);
        Assert.Equal(VehicleStatus.Kirada, (await vehicles.GetAsync(v))!.Durum);   // çıktı → Kirada

        await rentals.ReturnAsync(id, returnKm: 1200, returnFuel: 8, Bas.AddDays(2));
        Assert.Equal(VehicleStatus.Musait, (await vehicles.GetAsync(v))!.Durum);   // döndü → Musait (boşta)
    }

    [Fact]
    public async Task Iptal_araci_serbest_birakir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var vehicles = sp.GetRequiredService<VehicleService>();
        var (m, v) = await SeedAsync(sp, "34 DR 03");
        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m });
        await rentals.DeliverAsync(id, pickupKm: 1000, pickupFuel: 8); // Kirada
        await rentals.CancelAsync(id);
        Assert.Equal(VehicleStatus.Musait, (await vehicles.GetAsync(v))!.Durum); // iptal → serbest
    }

    [Fact]
    public async Task Kirada_arac_cakismayan_ileri_tarihte_musait_kalir()
    {
        // Havuz Kirada'yı dahil ettiğinden: çıkıştaki araç, döndükten SONRAKİ (çakışmayan) tarihte hâlâ müsait.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var avail = sp.GetRequiredService<AvailabilityService>();
        var (m, v) = await SeedAsync(sp, "34 DR 04");
        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m });
        await rentals.DeliverAsync(id, pickupKm: 1000, pickupFuel: 8); // araç Kirada, kira [Bas, Bas+2)

        // Çakışan aralık (kira dönemi) → müsait DEĞİL.
        var cakisan = await avail.FindAvailableAsync(Bas, Bas.AddDays(2));
        Assert.DoesNotContain(cakisan, x => x.Id == v);

        // Çakışmayan ileri aralık (dönüşten sonra) → HÂLÂ müsait (Kirada olmasına rağmen — bug DEĞİL).
        var ileri = await avail.FindAvailableAsync(Bas.AddDays(5), Bas.AddDays(7));
        Assert.Contains(ileri, x => x.Id == v);
    }

    [Fact]
    public async Task Kira_teslim_donus_vehicles_cache_i_gunceller()
    {
        // Latent cache fix: VehicleService.ListAsync() cache'lidir. Kira Teslim/Dönüş araç Durum'unu
        // değiştirince RentalService cache'i invalidate etmezse cache'li liste BAYAT Durum gösterirdi.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var vehicles = sp.GetRequiredService<VehicleService>();
        var (m, v) = await SeedAsync(sp, "34 DR 05");

        // Cache'i ISIT: ListAsync şimdi Musait'i cache'ler.
        Assert.Equal(VehicleStatus.Musait, (await vehicles.ListAsync()).Single(x => x.Id == v).Durum);

        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m });
        await rentals.DeliverAsync(id, pickupKm: 1000, pickupFuel: 8); // araç Kirada + cache invalidate

        // Cache'li liste artık TAZE Durum'u yansıtmalı (bayat Musait DEĞİL).
        Assert.Equal(VehicleStatus.Kirada, (await vehicles.ListAsync()).Single(x => x.Id == v).Durum);

        await rentals.ReturnAsync(id, returnKm: 1200, returnFuel: 8, Bas.AddDays(2)); // Musait + cache invalidate
        Assert.Equal(VehicleStatus.Musait, (await vehicles.ListAsync()).Single(x => x.Id == v).Durum);
    }
}
