using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap H3 — araç durum-takip (gün kırılımı). BAĞIMSIZ ORACLE: 1 araç, 1 kira 10–12 Haz.
/// 09–13 Haz sorgusu → 5 gün; 10/11/12 dolu (boş 0), 09 ve 13 boş (dolu 0). Toplam hep 1.
/// </summary>
[Collection("postgres")]
public sealed class RaporEksik3Tests(PostgresFixture fx)
{
    [Fact]
    public async Task Gun_kirilimi_dolu_bos()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var custId = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Takip Müşteri" });
        var vId = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 DT 01", Durum = VehicleStatus.Musait });

        var bas = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);
        await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = custId, VehicleId = vId, BasTar = bas, BitTar = bas.AddDays(2),
            GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m
        });

        var rows = await sp.GetRequiredService<ReportService>().GetAracDurumTakipAsync(
            new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(5, rows.Count);                       // 9,10,11,12,13
        Assert.All(rows, r => Assert.Equal(1, r.ToplamArac));
        var d10 = rows.Single(r => r.Gun.Date == new DateTime(2026, 6, 10));
        Assert.Equal(1, d10.Dolu);
        Assert.Equal(0, d10.Bos);
        var d9 = rows.Single(r => r.Gun.Date == new DateTime(2026, 6, 9));
        Assert.Equal(0, d9.Dolu);
        Assert.Equal(1, d9.Bos);
        var d13 = rows.Single(r => r.Gun.Date == new DateTime(2026, 6, 13));
        Assert.Equal(0, d13.Dolu);   // kira 12'de bitti
        Assert.Equal(1, d13.Bos);
    }
}
