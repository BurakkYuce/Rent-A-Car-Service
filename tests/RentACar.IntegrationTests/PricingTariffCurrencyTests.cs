using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Application.RateMatrices;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul bulgusu B-A1 (PARA, Kritik): TL tarife dövizli kirada ham sayı olarak döviz sayılıyordu
/// (5 gün × 850 TL tarife, EUR kira → 4.250 € yazılıyordu). Kural: tarife tutarı TARİFENİN dövizindedir,
/// kira dövizine kira başlangıç gününün kuruyla çevrilir; kur yoksa gürültülü red. Önizleme, kayıt ve
/// uzatma aynı değeri taşır.
///
/// BAĞIMSIZ ORACLE (elle): tarife Gün5 = 850 TL, sabit kur EUR = 40 → günlük 850 / 40 = 21,25 €;
/// 5 gün → 106,25 €; TL karşılığı 106,25 × 40 = 4.250 TL. Uzatma +2 gün → 106,25 + 2 × 21,25 = 148,75 €.
/// EUR tarife Gün5 = 100 € → EUR kirada çevrimsiz 100 €; TL kirada 100 × 40 = 4.000 TL.
/// </summary>
[Collection("postgres")]
public sealed class PricingTariffCurrencyTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = TestZaman.DaysLater(3);

    private static async Task<Guid> SeedAsync(IServiceProvider sp, string plate, string? matrixCurrency, decimal day5)
    {
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        var vehicleId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate, Grup = "EKO" });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-BASE", Ad = "Eko Baz", AracGrupKod = "EKO", ParaBirimi = matrixCurrency,
            Gun1 = day5, Gun2 = day5, Gun3 = day5, Gun4 = day5, Gun5 = day5, Gun6 = day5, Gun7 = day5,
            OnayDurumu = TariffApprovalStatus.Onayli
        });
        return vehicleId;
    }

    private static Task PinEurAsync(IServiceProvider sp) =>
        sp.GetRequiredService<FixedExchangeRateService>().UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });

    private static BookingInput Booking(Guid vehicleId, Guid account, string? currency, string? priceType = "Otomatik") => new()
    {
        MusteriId = account, VehicleId = vehicleId, BasTar = Start, BitTar = Start.AddDays(5),
        GunlukUcret = 0m, FiyatTuru = priceType, Doviz = currency
    };

    [Fact]
    public async Task TRY_tariff_in_EUR_rental_is_converted_in_preview_record_and_extension()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicleId = await SeedAsync(sp, "34 PD 01", "TRY", 850m);
        await PinEurAsync(sp);
        var account = await TestCustomer.NewAsync(sp);

        // Önizleme (kira formu paneli)
        var preview = await sp.GetRequiredService<RentalCalculationService>().CalculateAsync(new KiraHesapIstek(
            vehicleId, Start, Start.AddDays(5), null, "Otomatik", "EUR", null, [], MusteriId: account));
        Assert.True(preview.Ok, preview.Hata);
        Assert.Equal(21.25m, preview.GunlukUcret);
        Assert.Equal(106.25m, preview.Tutar);
        Assert.Equal(4250.00m, preview.GenelToplamTl);

        // Kayıt
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await rentals.CreateDirectAsync(Booking(vehicleId, account, "EUR"));
        var repo = sp.GetRequiredService<IBookingRepository>();
        var c = await repo.FindRentalAsync(id);
        Assert.Equal(21.25m, c!.GunlukUcret);
        Assert.Equal(106.25m, c.Tutar);
        Assert.Equal(40m, c.KurSnapshot);

        // Uzatma (+2 gün) kayıtlı EUR günlük ücretle büyür
        Assert.True(await rentals.ExtendAsync(id, Start.AddDays(7)));
        c = await repo.FindRentalAsync(id);
        Assert.Equal(148.75m, c!.Tutar);
    }

    [Fact]
    public async Task TRY_tariff_in_EUR_rental_legacy_blank_rate_path_is_converted()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicleId = await SeedAsync(sp, "34 PD 02", "TRY", 850m);
        await PinEurAsync(sp);

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(
            Booking(vehicleId, await TestCustomer.NewAsync(sp), "EURO", priceType: null));
        var c = await sp.GetRequiredService<IBookingRepository>().FindRentalAsync(id);
        Assert.Equal(21.25m, c!.GunlukUcret);
        Assert.Equal(106.25m, c.Tutar);
    }

    [Fact]
    public async Task Missing_rate_rejects_record_and_preview_noisily()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // GBP için ne sabit kur ne TCMB kaydı var (CrmCiroFxTests ile aynı varsayım).
        var vehicleId = await SeedAsync(sp, "34 PD 03", "TRY", 850m);
        var account = await TestCustomer.NewAsync(sp);

        await Assert.ThrowsAsync<ValidationException>(() =>
            sp.GetRequiredService<RentalService>().CreateDirectAsync(Booking(vehicleId, account, "GBP")));
        var preview = await sp.GetRequiredService<RentalCalculationService>().CalculateAsync(new KiraHesapIstek(
            vehicleId, Start, Start.AddDays(5), null, "Otomatik", "GBP", null, [], MusteriId: account));
        Assert.False(preview.Ok);
        Assert.Equal(0m, preview.Tutar);
    }

    [Fact]
    public async Task EUR_tariff_in_EUR_rental_is_used_without_conversion()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicleId = await SeedAsync(sp, "34 PD 04", "EUR", 100m);
        await PinEurAsync(sp); // KurSnapshot için; fiyat çevrilmez

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(
            Booking(vehicleId, await TestCustomer.NewAsync(sp), "EUR"));
        var c = await sp.GetRequiredService<IBookingRepository>().FindRentalAsync(id);
        Assert.Equal(100m, c!.GunlukUcret);
        Assert.Equal(500m, c.Tutar);
    }

    [Fact]
    public async Task EUR_tariff_in_TRY_rental_is_converted_to_lira()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicleId = await SeedAsync(sp, "34 PD 05", "EUR", 100m);
        await PinEurAsync(sp);

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(
            Booking(vehicleId, await TestCustomer.NewAsync(sp), "TL"));
        var c = await sp.GetRequiredService<IBookingRepository>().FindRentalAsync(id);
        Assert.Equal(4000m, c!.GunlukUcret);
        Assert.Equal(20000m, c.Tutar);
    }
}
