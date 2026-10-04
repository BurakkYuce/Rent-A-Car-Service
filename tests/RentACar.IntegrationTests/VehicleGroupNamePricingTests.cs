using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalAddOns;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul bulgusu B-A2 (Yüksek): araç ekrandan açılınca <c>Vehicle.Grup</c>'a grubun ADI yazılır (bilinçli
/// tasarım: grup sayacı, eşleşmeyen-değer paneli, "Ata" aracı, ad değişikliği zinciri ve vitrin hep Ad
/// üstünden). Fiyat tarafı ise KOD arıyordu → UI'dan açılan her araçta "Otomatik tarife bulunamadı",
/// genç/ek sürücü grup ücretleri de uygulanmıyordu. Kural: fiyat tarafı aracın grup değerini tanımlı
/// gruba çözer (önce Kod, sonra Ad) ve grubun KODUYLA çalışır.
///
/// BAĞIMSIZ ORACLE (elle): grup Kod "TSTG" / Ad "Test Grubu", genç sürücü eşiği 25 yaş, 100 NET/gün;
/// tarife (TSTG) Gün3 = 800 → 3 gün 2.400. 22 yaşında müşteri → genç sürücü satırı 3 × 100 = 300 net.
/// </summary>
[Collection("postgres")]
public sealed class VehicleGroupNamePricingTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = TestZaman.DaysLater(4);

    private static async Task<(Guid vehicle, Guid customer)> SeedAsync(IServiceProvider sp, string vehicleGroupValue, string plate)
    {
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        {
            Kod = "TSTG", Ad = "Test Grubu", GencSurucuYas = 25, GencSurucuUcretGunluk = 100m
        });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "TST-M", Ad = "Test", AracGrupKod = "TSTG", ParaBirimi = "TRY",
            Gun1 = 1000m, Gun2 = 900m, Gun3 = 800m, OnayDurumu = TariffApprovalStatus.Onayli
        });
        var vehicle = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate, Grup = vehicleGroupValue });
        var customer = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        {
            Tip = CustomerType.Bireysel, Ad = "Genç", Soyad = "Sürücü",
            DogumTarihi = new DateTimeOffset(DateTime.UtcNow.Date.AddYears(-22), TimeSpan.Zero)
        });
        return (vehicle, customer);
    }

    private static BookingInput Booking(Guid vehicle, Guid customer) => new()
    {
        MusteriId = customer, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3),
        GunlukUcret = 0m, FiyatTuru = "Otomatik"
    };

    [Theory]
    [InlineData("Test Grubu")]   // ekrandan açılan araç (grup adı)
    [InlineData("TEST GRUBU")]   // ad, büyük/küçük harf farkı
    [InlineData("TSTG")]         // API/içe aktarımla açılan araç (grup kodu) — regresyon
    public async Task Rental_prices_from_group_tariff_whether_vehicle_stores_name_or_code(string groupValue)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (vehicle, customer) = await SeedAsync(sp, groupValue, "34 GA 01");

        var preview = await sp.GetRequiredService<RentalCalculationService>().CalculateAsync(new KiraHesapIstek(
            vehicle, Start, Start.AddDays(3), null, "Otomatik", "TL", null, [], MusteriId: customer));
        Assert.True(preview.Ok, preview.Hata);
        Assert.Equal(800m, preview.GunlukUcret);
        Assert.Equal(2400m, preview.Tutar);
        var youngPreview = Assert.Single(preview.EkKalemler);
        Assert.Equal(300m, youngPreview.Net);

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(Booking(vehicle, customer));
        var c = await sp.GetRequiredService<IBookingRepository>().FindRentalAsync(id);
        Assert.Equal(800m, c!.GunlukUcret);
        Assert.Equal(2400m, c.Tutar);
        var young = Assert.Single(await sp.GetRequiredService<IRentalAddOnRepository>().ListForRentalAsync(id));
        Assert.Equal(300m, young.NetTutar);
    }

    /// <summary>Doluluk çarpanı grubun ADINI taşıyan araçları da sayar. ORACLE (elle): grupta 2 araç (biri kod "EKO",
    /// biri ad "Ekonomik" ile), ad taşıyan araç 5 günün 5'inde dolu → 5 / (2 × 5) = %50; kural eşik 50 / çarpan 10 →
    /// 1000 → 1.100/gün. Eskiden yalnız kodlu araç sayılıyordu → %0, çarpan yok (1.000).</summary>
    [Theory]
    [InlineData("EKO")]
    [InlineData("Ekonomik")]
    public async Task Occupancy_counts_vehicles_that_store_the_group_name(string requestGroup)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-M", Ad = "Eko", AracGrupKod = "EKO", ParaBirimi = "TRY",
            Gun1 = 1000m, Gun2 = 1000m, Gun3 = 1000m, Gun4 = 1000m, Gun5 = 1000m, OnayDurumu = TariffApprovalStatus.Onayli
        });
        var vehicles = sp.GetRequiredService<VehicleService>();
        await vehicles.CreateAsync(new VehicleInput { Plaka = "34 GA 11", Grup = "EKO" });
        var byName = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 GA 12", Grup = "Ekonomik" });
        var customer = await TestCustomer.NewAsync(sp);
        await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = customer, VehicleId = byName, BasTar = Start, BitTar = Start.AddDays(5), GunlukUcret = 100m });
        await sp.GetRequiredService<RentACar.Application.DolulukFiyat.OccupancyPriceRuleService>().CreateAsync(
            new RentACar.Application.DolulukFiyat.DolulukFiyatKuralInput { Kod = "D50", Ad = "Doluluk 50", EsikYuzde = 50, CarpanYuzde = 10m });

        var q = await sp.GetRequiredService<RentACar.Application.Pricing.RentalQuoteEngine>().QuoteAsync(
            new RentACar.Application.Pricing.QuoteRequest { AracGrupKod = requestGroup, BasTar = Start, BitTar = Start.AddDays(5) });
        Assert.Equal(1100m, q.GunlukUcret);
    }
}
