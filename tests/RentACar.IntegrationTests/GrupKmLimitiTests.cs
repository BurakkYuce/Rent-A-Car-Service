using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.ReservationSources;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul bulgusu d-rapor-km-detay-03 (PARA): araç grubunun km limiti kiraya uygulanmıyordu — Lüks grubu
/// 250 km/gün + 5 TL aşım; 3 günlük kira 10.000 → 10.850 km: Km limiti 0, fazla km 0, bedel 0.
///
/// Kural: kira açılırken km limiti boşsa (0) grubun GÜNLÜK limiti × kira günü ve aşım ücreti kiraya
/// SNAPSHOT olarak kopyalanır; elle girilen değer önceliklidir (alan bazında). Doğrudan kira, rezervasyondan
/// dönüşüm ve önizleme aynı kuralı izler. KM sınırsız kaynak kuralı yine son sözü söyler.
///
/// BAĞIMSIZ ORACLE (elle): limit 250 × 3 = 750; kat edilen 850; aşım 850 − 750 = 100 km; bedel 100 × 5 = 500.
/// </summary>
[Collection("postgres")]
public sealed class GrupKmLimitiTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = TestZaman.DaysLater(-10);

    private static async Task<(Guid Customer, Guid Vehicle)> PartiesAsync(IServiceProvider sp, string plate)
    {
        var groups = sp.GetRequiredService<VehicleGroupService>();
        if (!(await groups.ListAsync()).Any(g => g.Kod == "LUKS"))
            await groups.CreateAsync(new VehicleGroupInput
            {
                Kod = "LUKS", Ad = "Lüks", GunlukKmLimiti = 250, AsimKmUcreti = 5m,
            });
        var vehicle = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate, Grup = "LUKS" });
        return (await TestCustomer.NewAsync(sp), vehicle);
    }

    private static BookingInput Request(Guid customer, Guid vehicle) => new()
    {
        MusteriId = customer, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m,
    };

    [Fact]
    public async Task Bos_limitli_kirada_grup_limiti_kopyalanir_ve_asim_ucretlenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var (customer, vehicle) = await PartiesAsync(sp, "34 GK 01");

        var id = await rentals.CreateDirectAsync(Request(customer, vehicle));
        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(750, c.KmLimit);
        Assert.Equal(5m, c.FazlaKmUcret);

        await rentals.DeliverAsync(id, pickupKm: 10_000, pickupFuel: 8);
        var preview = await rentals.PreviewReturnAsync(id, 10_850, 8, Start.AddDays(3));
        Assert.Equal(100, preview.FazlaKm);
        Assert.Equal(500m, preview.FazlaKmBedeli);

        await rentals.ReturnAsync(id, 10_850, 8, Start.AddDays(3));
        var done = (await rentals.GetAsync(id))!;
        Assert.Equal(100, done.FazlaKm);
        Assert.Equal(500m, done.FazlaKmBedeli);
        Assert.Equal(3000m + 500m, done.GenelToplam);
    }

    [Fact]
    public async Task Elle_girilen_limit_ve_ucret_onceliklidir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var (customer, vehicle) = await PartiesAsync(sp, "34 GK 02");

        var request = Request(customer, vehicle);
        request.KmLimit = 1000; request.FazlaKmUcret = 2m;
        var c = (await rentals.GetAsync(await rentals.CreateDirectAsync(request)))!;
        Assert.Equal(1000, c.KmLimit);
        Assert.Equal(2m, c.FazlaKmUcret);

        // Alan bazında öncelik: yalnız limit elle girildiyse ücret gruptan gelir.
        var (customer2, vehicle2) = await PartiesAsync(sp, "34 GK 03");
        var onlyLimit = Request(customer2, vehicle2);
        onlyLimit.KmLimit = 1000;
        var c2 = (await rentals.GetAsync(await rentals.CreateDirectAsync(onlyLimit)))!;
        Assert.Equal(1000, c2.KmLimit);
        Assert.Equal(5m, c2.FazlaKmUcret);
    }

    [Fact]
    public async Task Rezervasyondan_donusumde_ayni_kural_uygulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var reservations = sp.GetRequiredService<ReservationService>();
        var rentals = sp.GetRequiredService<RentalService>();
        var (customer, vehicle) = await PartiesAsync(sp, "34 GK 04");

        var request = Request(customer, vehicle);
        request.BasTar = TestZaman.DaysLater(2);
        request.BitTar = TestZaman.DaysLater(5);
        var resId = await reservations.CreateAsync(request);
        var rental = (await rentals.GetAsync(await reservations.ConvertToRentalAsync(resId)))!;
        Assert.Equal(750, rental.KmLimit);
        Assert.Equal(5m, rental.FazlaKmUcret);
    }

    [Fact]
    public async Task Km_sinirsiz_kaynak_grup_limitini_ezer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        await sp.GetRequiredService<ReservationSourceService>().CreateAsync(
            new ReservationSourceInput { Kod = "SINIRSIZ", Ad = "Sınırsız", KmSinirsiz = true });
        var (customer, vehicle) = await PartiesAsync(sp, "34 GK 05");

        var request = Request(customer, vehicle);
        request.Kaynak = "SINIRSIZ";
        var c = (await rentals.GetAsync(await rentals.CreateDirectAsync(request)))!;
        Assert.Equal(0, c.KmLimit);
        Assert.True(c.KmSinirsiz);
        Assert.Null(c.KmLimitGunluk);
    }

    [Fact]
    public async Task Onizleme_kayitla_ayni_limiti_bildirir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (_, vehicle) = await PartiesAsync(sp, "34 GK 06");

        var result = await sp.GetRequiredService<RentalCalculationService>().CalculateAsync(new KiraHesapIstek(
            VehicleId: vehicle, BasTar: Start, BitTar: Start.AddDays(3), GunlukUcret: 1000m, FiyatTuru: null,
            Doviz: null, CikisOfisi: null, EkHizmetler: []));
        Assert.True(result.Ok);
        Assert.Equal(750, result.KmLimit);
        Assert.Equal(5m, result.FazlaKmUcret);
    }
}
