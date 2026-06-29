using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap I2 — rezervasyon düzenleme (UpdateAsync). BAĞIMSIZ ORACLE: 3 gün×100=300 → düzenle 5 gün×150=750;
/// yalnız Rezerv/Onaylı düzenlenir (İptal red); aktif kira çakışması red. Defter etkilemez.
/// </summary>
[Collection("postgres")]
public sealed class RezDuzenlemeTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Duzenle_yeniden_fiyatla()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cust = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Rez" });
        var v1 = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 RD 01", Durum = VehicleStatus.Musait });
        var v2 = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 RD 02", Durum = VehicleStatus.Musait });
        var rez = sp.GetRequiredService<ReservationService>();

        var id = await rez.CreateAsync(new BookingInput { MusteriId = cust, VehicleId = v1, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m });
        Assert.True(await rez.UpdateAsync(id, new BookingInput
        { MusteriId = cust, VehicleId = v2, BasTar = Bas, BitTar = Bas.AddDays(5), GunlukUcret = 150m, Kaynak = "Web" }));

        var r = await rez.GetAsync(id);
        Assert.Equal(5, r!.Gun);
        Assert.Equal(750m, r.Tutar);     // 5 gün × 150 (elle oracle)
        Assert.Equal(v2, r.VehicleId);
        Assert.Equal("Web", r.Kaynak);
    }

    [Fact]
    public async Task Iptal_edilmis_duzenlenemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cust = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Rez" });
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 RD 03", Durum = VehicleStatus.Musait });
        var rez = sp.GetRequiredService<ReservationService>();
        var id = await rez.CreateAsync(new BookingInput { MusteriId = cust, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m });
        await rez.CancelAsync(id);

        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() =>
            rez.UpdateAsync(id, new BookingInput { MusteriId = cust, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m }));
    }
}
