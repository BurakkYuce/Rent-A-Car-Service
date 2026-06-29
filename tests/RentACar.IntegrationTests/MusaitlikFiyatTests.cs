using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Availability;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap G4 — müsaitlik fiyatlı (salt-okur). BAĞIMSIZ ORACLE: /musaitlik'in kullandığı veri yolu —
/// müsait araç bulunur + grubunun fiyat motoru teklifi (günlük/toplam) doğru. (Sayfa bu ikisini birleştirir.)
/// </summary>
[Collection("postgres")]
public sealed class MusaitlikFiyatTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset From = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Available_vehicle_has_group_price()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-M", Ad = "Eko", AracGrupKod = "EKO",
            Gun1 = 100m, Gun2 = 100m, Gun3 = 100m, Gun4 = 100m, Gun5 = 100m, Gun6 = 100m, Gun7 = 100m,
            OnayDurumu = TarifeOnayDurumu.Onayli
        });
        await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 MS 01", Grup = "EKO", Durum = VehicleStatus.Musait });

        // (1) müsait araç bulunur
        var available = await sp.GetRequiredService<AvailabilityService>().FindAvailableAsync(From, From.AddDays(7), null, null);
        var arac = Assert.Single(available);
        Assert.Equal("34MS01", arac.Plaka); // plaka normalize edilir (boşluk silinir)

        // (2) grubunun fiyat motoru teklifi (7 gün × 100 = 700)
        var q = await sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(new QuoteRequest
        { AracGrupKod = arac.Grup!, BasTar = From, BitTar = From.AddDays(7), SigortaUrunKodlari = [] });
        Assert.Equal(100m, q.GunlukUcret);
        Assert.Equal(700m, q.GenelToplam);
    }
}
