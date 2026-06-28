using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Search;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap C4 — Global arama. BAĞIMSIZ ORACLE: araç(Marka)/cari(Ad)/kira(No)/rezervasyon(No) bulunur;
/// tenant izolasyon; kısa/boş sorgu boş döner.
/// </summary>
[Collection("postgres")]
public sealed class SearchTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Finds_across_modules()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var vId = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 ARA 99", Marka = "AraMarkaX" });
        var cId = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "AraCariX" });
        var rId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cId, VehicleId = vId, BasTar = Bas, BitTar = Bas.AddDays(4), GunlukUcret = 100m });
        var rezId = await sp.GetRequiredService<ReservationService>().CreateAsync(new BookingInput
        { MusteriId = cId, VehicleId = vId, BasTar = Bas.AddDays(30), BitTar = Bas.AddDays(33), GunlukUcret = 100m });

        var rentalNo = (await sp.GetRequiredService<IBookingRepository>().FindRentalAsync(rId))!.SozlesmeNo;
        var rezNo = (await sp.GetRequiredService<ReservationService>().GetAsync(rezId))!.ReservationNo;

        var search = sp.GetRequiredService<SearchService>();
        Assert.Contains(await search.SearchAsync("AraMarkaX"), h => h.Tur == "Araç");
        Assert.Contains(await search.SearchAsync("AraCariX"), h => h.Tur == "Cari");
        Assert.Contains(await search.SearchAsync(rentalNo), h => h.Tur == "Kira" && h.Baslik == rentalNo);
        Assert.Contains(await search.SearchAsync(rezNo), h => h.Tur == "Rezervasyon" && h.Baslik == rezNo);
    }

    [Fact]
    public async Task Short_or_empty_query_returns_empty()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var search = scope.ServiceProvider.GetRequiredService<SearchService>();

        Assert.Empty(await search.SearchAsync("a"));   // <2 karakter
        Assert.Empty(await search.SearchAsync(""));
        Assert.Empty(await search.SearchAsync(null));
    }

    [Fact]
    public async Task Tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = "34 IZO 01", Marka = "IzolasyonMarka" });

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<SearchService>().SearchAsync("IzolasyonMarka"));
    }
}
