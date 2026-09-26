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
    // Göreli: rezervasyon başlangıcı geçmişe kapalı (TarihPolitikasi) — sabit tarih takvimle kırmızıya döner.
    private static readonly DateTimeOffset Start = TestZaman.DaysLater(10);

    [Fact]
    public async Task Duzenle_yeniden_fiyatla()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cust = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Rez" });
        var v1 = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 RD 01", Durum = VehicleStatus.Musait });
        var v2 = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 RD 02", Durum = VehicleStatus.Musait });
        var res = sp.GetRequiredService<ReservationService>();

        var id = await res.CreateAsync(new BookingInput { MusteriId = cust, VehicleId = v1, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 100m });
        Assert.True(await res.UpdateAsync(id, new BookingInput
        { MusteriId = cust, VehicleId = v2, BasTar = Start, BitTar = Start.AddDays(5), GunlukUcret = 150m, Kaynak = "Web" }));

        var r = await res.GetAsync(id);
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
        var cust = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Rez" });
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 RD 03", Durum = VehicleStatus.Musait });
        var res = sp.GetRequiredService<ReservationService>();
        var id = await res.CreateAsync(new BookingInput { MusteriId = cust, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(2), GunlukUcret = 100m });
        await res.CancelAsync(id);

        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() =>
            res.UpdateAsync(id, new BookingInput { MusteriId = cust, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 100m }));
    }
}
