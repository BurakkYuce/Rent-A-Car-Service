using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Vehicles;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap A2 — Kira/rez ödeme-derinlik alanları (Provizyon/Depozito/Komisyon/Drop/SonraÖde). BAĞIMSIZ
/// ORACLE: alanlar roundtrip olur; KRİTİK invariant — deftere/bakiyeye YANSIMAZ (GenelToplam/Bakiye
/// yalnız gün×ücret); rezervasyon→kira çevrimi alanları taşır.
/// </summary>
[Collection("postgres")]
public sealed class OdemeDerinlikTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    private static BookingInput Booking(Guid vehicleId) => new()
    {
        MusteriId = Guid.NewGuid(), VehicleId = vehicleId,
        BasTar = Bas, BitTar = Bas.AddDays(4), GunlukUcret = 100m, // 4 gün × 100 = 400 (manuel)
        Provizyon = 2000m, Depozito = 750m, KomisyonOran = 12.5m, KomisyonTutar = 200m,
        DropUcreti = 150m, SonraOdeOran = 40m
    };

    [Fact]
    public async Task Rental_fields_roundtrip_and_not_in_balance()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vehicleId = await scope.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 OD 01" });
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(Booking(vehicleId));
        var c = await scope.ServiceProvider.GetRequiredService<IBookingRepository>().FindRentalAsync(id);

        Assert.NotNull(c);
        Assert.Equal(2000m, c!.Provizyon);
        Assert.Equal(750m, c.Depozito);
        Assert.Equal(12.5m, c.KomisyonOran);
        Assert.Equal(200m, c.KomisyonTutar);
        Assert.Equal(150m, c.DropUcreti);
        Assert.Equal(40m, c.SonraOdeOran);
        // KRİTİK: provizyon/depozito vb. bakiyeye/toplama YANSIMAZ — yalnız gün×ücret.
        Assert.Equal(400m, c.Tutar);
        Assert.Equal(400m, c.GenelToplam);
        Assert.Equal(400m, c.Bakiye);
    }

    [Fact]
    public async Task Reservation_fields_roundtrip_and_convert_carries()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vehicleId = await scope.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 OD 02" });
        var res = scope.ServiceProvider.GetRequiredService<ReservationService>();

        var resId = await res.CreateAsync(Booking(vehicleId));
        var r = await res.GetAsync(resId);
        Assert.Equal(2000m, r!.Provizyon);
        Assert.Equal(40m, r.SonraOdeOran);
        Assert.Equal(400m, r.Tutar); // ödeme-derinlik tutarı etkilemez

        // Rezervasyon → kira: alanlar taşınır.
        var rentalId = await res.ConvertToRentalAsync(resId);
        var c = await scope.ServiceProvider.GetRequiredService<IBookingRepository>().FindRentalAsync(rentalId);
        Assert.Equal(2000m, c!.Provizyon);
        Assert.Equal(750m, c.Depozito);
        Assert.Equal(12.5m, c.KomisyonOran);
        Assert.Equal(150m, c.DropUcreti);
        Assert.Equal(400m, c.GenelToplam); // çevrimde de bakiyeye yansımaz
    }

    [Fact]
    public async Task Fields_optional_default_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vehicleId = await scope.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 OD 03" });
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = Guid.NewGuid(), VehicleId = vehicleId, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m });
        var c = await scope.ServiceProvider.GetRequiredService<IBookingRepository>().FindRentalAsync(id);
        Assert.Null(c!.Provizyon);
        Assert.Null(c.Depozito);
        Assert.Null(c.SonraOdeOran);
    }
}
