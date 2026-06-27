using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Rezervasyon takvimi (salt-okunur doluluk) — bağımsız oracle. Bilinen rezervasyon + kira
/// ekle, takvim aralığının onları doğru çubuklarla döndürdüğünü doğrula. Aralık dışı/iptal
/// hariç; tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class CalendarTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset JanFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset JanTo = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> Cari(IServiceScope s)
        => await s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Tak", Soyad = "Vim" });

    private static async Task<Guid> Vehicle(IServiceScope s, string plaka)
        => await s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plaka });

    [Fact]
    public async Task Occupancy_returns_reservation_and_active_rental_spans()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rez = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var kira = scope.ServiceProvider.GetRequiredService<RentalService>();
        var cal = scope.ServiceProvider.GetRequiredService<CalendarService>();

        var cari = await Cari(scope);
        var aracA = await Vehicle(scope, "34CALA1");
        var aracB = await Vehicle(scope, "34CALB2");

        // Rezervasyon A: 1–4 Ocak. Kira B: 10–12 Ocak (doğrudan, Kirada).
        await rez.CreateAsync(new BookingInput
        { MusteriId = cari, VehicleId = aracA, BasTar = JanFrom.AddDays(0).AddHours(9), BitTar = JanFrom.AddDays(3).AddHours(9), GunlukUcret = 100m });
        await kira.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = aracB, BasTar = JanFrom.AddDays(9).AddHours(9), BitTar = JanFrom.AddDays(11).AddHours(9), GunlukUcret = 100m });

        var spans = await cal.GetOccupancyAsync(JanFrom, JanTo);
        Assert.Equal(2, spans.Count);

        var a = Assert.Single(spans, x => x.VehicleId == aracA);
        Assert.Equal("Rezervasyon", a.Tip);
        var b = Assert.Single(spans, x => x.VehicleId == aracB);
        Assert.Equal("Kira", b.Tip);
        Assert.Equal(RentalStatus.Kirada.ToString(), b.Durum);
    }

    [Fact]
    public async Task Out_of_range_reservation_excluded()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rez = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var cal = scope.ServiceProvider.GetRequiredService<CalendarService>();

        var cari = await Cari(scope);
        var arac = await Vehicle(scope, "34CALC3");

        // Mart rezervasyonu — Ocak penceresine düşmez.
        await rez.CreateAsync(new BookingInput
        { MusteriId = cari, VehicleId = arac, BasTar = new DateTimeOffset(2026, 3, 5, 9, 0, 0, TimeSpan.Zero), BitTar = new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero), GunlukUcret = 100m });

        Assert.Empty(await cal.GetOccupancyAsync(JanFrom, JanTo));
    }

    [Fact]
    public async Task Cancelled_reservation_excluded()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rez = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var cal = scope.ServiceProvider.GetRequiredService<CalendarService>();

        var cari = await Cari(scope);
        var arac = await Vehicle(scope, "34CALD4");

        var id = await rez.CreateAsync(new BookingInput
        { MusteriId = cari, VehicleId = arac, BasTar = JanFrom.AddDays(4).AddHours(9), BitTar = JanFrom.AddDays(6).AddHours(9), GunlukUcret = 100m });
        await rez.CancelAsync(id);

        // İptal rezervasyon doluluk çubuğu üretmez.
        Assert.Empty(await cal.GetOccupancyAsync(JanFrom, JanTo));
    }

    [Fact]
    public async Task Occupancy_is_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            var c = await Cari(s1);
            var v = await Vehicle(s1, "34CALT1");
            await s1.ServiceProvider.GetRequiredService<ReservationService>().CreateAsync(new BookingInput
            { MusteriId = c, VehicleId = v, BasTar = JanFrom.AddDays(1).AddHours(9), BitTar = JanFrom.AddDays(2).AddHours(9), GunlukUcret = 100m });
        }

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<CalendarService>().GetOccupancyAsync(JanFrom, JanTo));
    }
}
