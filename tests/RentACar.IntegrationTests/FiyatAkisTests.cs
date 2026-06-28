using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap A1 — Fiyat motorunun (RentalQuoteEngine) kira akışına bağlanması. BAĞIMSIZ ORACLE:
/// kira oluştururken manuel ücret yoksa tarife matrisinden (onaylı) gün-kademesi fiyatı otomatik gelir;
/// manuel ücret DAİMA kazanır; matris yoksa 0 (manuel). Beklenenler senaryodan.
/// </summary>
[Collection("postgres")]
public sealed class FiyatAkisTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedVehicleWithMatrixAsync(IServiceProvider sp, bool matrisOnayli = true, bool matris = true)
    {
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        var vehicleId = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 FA 01", Grup = "EKO" });
        if (matris)
            await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
            {
                Kod = "EKO-BASE", Ad = "Eko Baz", AracGrupKod = "EKO", // Kanal null → booking akışı (kanalsız) eşleşir
                Gun1 = 1000m, Gun2 = 950m, Gun3 = 900m, Gun4 = 875m, Gun5 = 850m, Gun6 = 825m, Gun7 = 800m,
                OnayDurumu = matrisOnayli ? TarifeOnayDurumu.Onayli : TarifeOnayDurumu.Bekliyor
            });
        return vehicleId;
    }

    private static BookingInput Booking(Guid vehicleId, decimal manuelUcret) => new()
    {
        MusteriId = Guid.NewGuid(), VehicleId = vehicleId,
        BasTar = Bas, BitTar = Bas.AddDays(5), GunlukUcret = manuelUcret // 5 gün → kademe Gün5
    };

    [Fact]
    public async Task Rental_auto_prices_from_tarife_matrisi_when_no_manual()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vehicleId = await SeedVehicleWithMatrixAsync(scope.ServiceProvider);
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(Booking(vehicleId, manuelUcret: 0m));

        var c = await scope.ServiceProvider.GetRequiredService<IBookingRepository>().FindRentalAsync(id);
        Assert.NotNull(c);
        Assert.Equal(850.00m, c!.GunlukUcret);   // Gün5 kademesi (oracle)
        Assert.Equal(5, c.Gun);
        Assert.Equal(4250.00m, c.Tutar);          // 850 × 5
    }

    [Fact]
    public async Task Manual_rate_always_wins_over_engine()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vehicleId = await SeedVehicleWithMatrixAsync(scope.ServiceProvider);
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(Booking(vehicleId, manuelUcret: 500m));

        var c = await scope.ServiceProvider.GetRequiredService<IBookingRepository>().FindRentalAsync(id);
        Assert.Equal(500.00m, c!.GunlukUcret);    // manuel kazanır (motor 850 vermesine rağmen)
        Assert.Equal(2500.00m, c.Tutar);
    }

    [Fact]
    public async Task Unapproved_matrix_not_used_falls_to_zero()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vehicleId = await SeedVehicleWithMatrixAsync(scope.ServiceProvider, matrisOnayli: false);
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(Booking(vehicleId, manuelUcret: 0m));

        var c = await scope.ServiceProvider.GetRequiredService<IBookingRepository>().FindRentalAsync(id);
        // Onaysız matris kullanılmaz, RateCard da yok → 0 (manuel girilmeli).
        Assert.Equal(0m, c!.GunlukUcret);
        Assert.Equal(0m, c.Tutar);
    }

    [Fact]
    public async Task Foreign_currency_matrix_not_auto_applied() // HIGH-1
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vehicleId = await SeedVehicleWithMatrixAsync(scope.ServiceProvider, matris: false);
        await scope.ServiceProvider.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        { Kod = "EUR-M", Ad = "Euro", AracGrupKod = "EKO", Gun5 = 100m, ParaBirimi = "EUR", OnayDurumu = TarifeOnayDurumu.Onayli });
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(Booking(vehicleId, manuelUcret: 0m));
        var c = await scope.ServiceProvider.GetRequiredService<IBookingRepository>().FindRentalAsync(id);
        // EUR matris booking'e ham TRY olarak YAZILMAZ → 0 (manuel girilmeli).
        Assert.Equal(0m, c!.GunlukUcret);
    }

    [Fact]
    public async Task Channel_specific_matrix_used_in_booking() // HIGH-2
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vehicleId = await SeedVehicleWithMatrixAsync(scope.ServiceProvider, matris: false);
        await scope.ServiceProvider.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "WEB-M", Ad = "Web", AracGrupKod = "EKO", Kanal = "WEB",
            Gun1 = 1000m, Gun2 = 950m, Gun3 = 900m, Gun4 = 875m, Gun5 = 850m, Gun6 = 825m, Gun7 = 800m,
            OnayDurumu = TarifeOnayDurumu.Onayli
        });
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(Booking(vehicleId, manuelUcret: 0m));
        var c = await scope.ServiceProvider.GetRequiredService<IBookingRepository>().FindRentalAsync(id);
        // Kanal-özel (WEB) matris artık kanalsız booking'de de kullanılıyor (eskiden sessizce 0'dı).
        Assert.Equal(850.00m, c!.GunlukUcret);
    }

    [Fact]
    public async Task Matched_matrix_does_not_fall_to_ratecard() // MEDIUM-1
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vehicleId = await SeedVehicleWithMatrixAsync(scope.ServiceProvider, matris: false);
        // Eşleşen ama gün-kademesi BOŞ matris (tüm Gun null).
        await scope.ServiceProvider.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        { Kod = "EMPTY-M", Ad = "Boş", AracGrupKod = "EKO", OnayDurumu = TarifeOnayDurumu.Onayli });
        // Aynı gruba eski RateCard 333 (fallback'e DÜŞMEMELİ çünkü matris eşleşti).
        await scope.ServiceProvider.GetRequiredService<RateCardService>().CreateAsync(new RateCardInput
        { Kod = "RC-EKO", Ad = "Eski", Grup = "EKO", MinGun = 1, MaxGun = 999, GunlukUcret = 333m, Doviz = "TRY", Aktif = true });
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(Booking(vehicleId, manuelUcret: 0m));
        var c = await scope.ServiceProvider.GetRequiredService<IBookingRepository>().FindRentalAsync(id);
        // Matris eşleşti (boş kademe) → 0; eski RateCard 333'e maskelenMEZ.
        Assert.Equal(0m, c!.GunlukUcret);
    }

    [Fact]
    public async Task Reservation_auto_prices_from_engine()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vehicleId = await SeedVehicleWithMatrixAsync(scope.ServiceProvider);
        var res = scope.ServiceProvider.GetRequiredService<ReservationService>();

        var id = await res.CreateAsync(Booking(vehicleId, manuelUcret: 0m));
        var r = await res.GetAsync(id);
        Assert.Equal(850.00m, r!.GunlukUcret);
    }
}
