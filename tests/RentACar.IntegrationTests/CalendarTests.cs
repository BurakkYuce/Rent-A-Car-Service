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
    // now-göreli pencere (rezervasyonlar gelecekte olmalı — TarihPolitikasi); ~31 günlük aralık korunur.
    private static readonly DateTimeOffset JanFrom = DateTimeOffset.UtcNow.AddDays(1);
    private static readonly DateTimeOffset JanTo = JanFrom.AddDays(31);

    private static async Task<Guid> Account(IServiceScope s)
        => await s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Tak", Soyad = "Vim" });

    private static async Task<Guid> Vehicle(IServiceScope s, string plate)
        => await s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate });

    [Fact]
    public async Task Occupancy_returns_reservation_and_active_rental_spans()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var res = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var rental = scope.ServiceProvider.GetRequiredService<RentalService>();
        var cal = scope.ServiceProvider.GetRequiredService<CalendarService>();

        var account = await Account(scope);
        var vehicleA = await Vehicle(scope, "34CALA1");
        var vehicleB = await Vehicle(scope, "34CALB2");

        // Rezervasyon A: 1–4 Ocak. Kira B: 10–12 Ocak (doğrudan, Kirada).
        await res.CreateAsync(new BookingInput
        { MusteriId = account, VehicleId = vehicleA, BasTar = JanFrom.AddDays(0).AddHours(9), BitTar = JanFrom.AddDays(3).AddHours(9), GunlukUcret = 100m });
        await rental.CreateDirectAsync(new BookingInput
        { MusteriId = account, VehicleId = vehicleB, BasTar = JanFrom.AddDays(9).AddHours(9), BitTar = JanFrom.AddDays(11).AddHours(9), GunlukUcret = 100m });

        var spans = await cal.GetOccupancyAsync(JanFrom, JanTo);
        Assert.Equal(2, spans.Count);

        var a = Assert.Single(spans, x => x.VehicleId == vehicleA);
        Assert.Equal("Rezervasyon", a.Tip);
        var b = Assert.Single(spans, x => x.VehicleId == vehicleB);
        Assert.Equal("Kira", b.Tip);
        Assert.Equal(RentalStatus.Kirada.ToString(), b.Durum);
    }

    [Fact]
    public async Task Out_of_range_reservation_excluded()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var res = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var cal = scope.ServiceProvider.GetRequiredService<CalendarService>();

        var account = await Account(scope);
        var vehicle = await Vehicle(scope, "34CALC3");

        // Mart rezervasyonu — Ocak penceresine düşmez.
        await res.CreateAsync(new BookingInput
        { MusteriId = account, VehicleId = vehicle, BasTar = JanFrom.AddDays(63).AddHours(9), BitTar = JanFrom.AddDays(66).AddHours(9), GunlukUcret = 100m }); // pencere DIŞI (63 > 31 gün) + gelecek

        Assert.Empty(await cal.GetOccupancyAsync(JanFrom, JanTo));
    }

    [Fact]
    public async Task Cancelled_reservation_excluded()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var res = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var cal = scope.ServiceProvider.GetRequiredService<CalendarService>();

        var account = await Account(scope);
        var vehicle = await Vehicle(scope, "34CALD4");

        var id = await res.CreateAsync(new BookingInput
        { MusteriId = account, VehicleId = vehicle, BasTar = JanFrom.AddDays(4).AddHours(9), BitTar = JanFrom.AddDays(6).AddHours(9), GunlukUcret = 100m });
        await res.CancelAsync(id);

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
            var c = await Account(s1);
            var v = await Vehicle(s1, "34CALT1");
            await s1.ServiceProvider.GetRequiredService<ReservationService>().CreateAsync(new BookingInput
            { MusteriId = c, VehicleId = v, BasTar = JanFrom.AddDays(1).AddHours(9), BitTar = JanFrom.AddDays(2).AddHours(9), GunlukUcret = 100m });
        }

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<CalendarService>().GetOccupancyAsync(JanFrom, JanTo));
    }
}
