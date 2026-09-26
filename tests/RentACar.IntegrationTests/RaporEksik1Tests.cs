using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap H1 — periyodik servis + KM detay raporları (salt-okur). BAĞIMSIZ ORACLE:
/// kalan km = sonraki bakım km − güncel km (60000−50000=10000); katedilen km = dönüş − çıkış (50500−50000=500).
/// </summary>
[Collection("postgres")]
public sealed class RaporEksik1Tests(PostgresFixture fx)
{
    [Fact]
    public async Task Periyodik_servis_kalan_km()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vId = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 PS 01", Km = 50000, Durum = VehicleStatus.Musait });

        var svc = sp.GetRequiredService<ServiceRecordService>();
        var sId = await svc.CreateAsync(new ServiceRecordInput { VehicleId = vId, Tip = ServiceType.Periyodik, GirisKm = 50000 });
        await svc.StartAsync(sId);
        await svc.CompleteAsync(sId, pickupKm: 50000, nextMaintenanceKm: 60000);

        var rows = await sp.GetRequiredService<ReportService>().GetPeriodicServiceAsync();
        var r = Assert.Single(rows);
        Assert.Equal(50000, r.GuncelKm);
        Assert.Equal(60000, r.SonrakiBakimKm);
        Assert.Equal(10000, r.KalanKm);   // 60000 − 50000 (elle oracle)
    }

    [Fact]
    public async Task Km_detay_katedilen()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var custId = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "KM Müşteri" });
        var vId = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 KM 02", Durum = VehicleStatus.Musait });

        var rentals = sp.GetRequiredService<RentalService>();
        var bas = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
        var bit = bas.AddDays(3);
        var rId = await rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = custId, VehicleId = vId, BasTar = bas, BitTar = bit,
            GunlukUcret = 100m, KmLimit = 300, FazlaKmUcret = 5m
        });
        await rentals.DeliverAsync(rId, pickupKm: 50000, pickupFuel: 12);
        await rentals.ReturnAsync(rId, returnKm: 50500, returnFuel: 12, actualReturn: bit);

        var rows = await sp.GetRequiredService<ReportService>().GetKmDetailAsync();
        var r = Assert.Single(rows, x => x.RentalId == rId);
        Assert.Equal(50000, r.CikisKm);
        Assert.Equal(50500, r.DonusKm);
        Assert.Equal(500, r.KatedilenKm);   // 50500 − 50000 (elle oracle)
    }
}
