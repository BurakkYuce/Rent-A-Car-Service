using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Baflar;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap N3 — CRM segment + personel çalışma (salt-okur agrega). BAĞIMSIZ ORACLE: 1 müşteri 2 kira (3 gün×100)
/// → kira 2 / ciro 600 / Standart; 1 personel 2 BAF → tahsis 2.
/// </summary>
[Collection("postgres")]
public sealed class CrmAnalizTests(PostgresFixture fx)
{
    [Fact]
    public async Task Musteri_segment_agrega()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var custId = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Segment Müşteri" });
        var vehicles = sp.GetRequiredService<VehicleService>();
        var rentals = sp.GetRequiredService<RentalService>();
        var bas = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);

        foreach (var plaka in new[] { "34 CR 01", "34 CR 02" })
        {
            var vId = await vehicles.CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });
            await rentals.CreateDirectAsync(new BookingInput
            { MusteriId = custId, VehicleId = vId, BasTar = bas, BitTar = bas.AddDays(3), GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m });
        }

        var rows = await sp.GetRequiredService<ReportService>().GetMusteriSegmentAsync();
        var r = Assert.Single(rows, x => x.CariId == custId);
        Assert.Equal(2, r.KiraSayisi);
        Assert.Equal(600m, r.ToplamCiro);   // 2 × (3 gün × 100)
        Assert.Equal("Standart", r.Segment); // 600 < 10000
    }

    [Fact]
    public async Task Personel_calisma_baf_sayisi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var baf = sp.GetRequiredService<BafService>();
        var personelId = Guid.NewGuid();

        await baf.CreateAsync(new BafInput { PersonelId = personelId, VehicleId = Guid.NewGuid(), CikisKm = 100 });
        await baf.CreateAsync(new BafInput { PersonelId = personelId, VehicleId = Guid.NewGuid(), CikisKm = 200 });

        var rows = await sp.GetRequiredService<ReportService>().GetPersonelCalismaAsync();
        var r = Assert.Single(rows, x => x.PersonelId == personelId);
        Assert.Equal(2, r.TahsisSayisi);
    }
}
