using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Expenses;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ2-2.2 — Tut/Sat (defleet) sinyali. Üç kural, eldeki veriden (eşikler: değer-oranı 0.45,
/// sınıf-katı 1.5). BAĞIMSIZ ORACLE (elle): (a) 12-ay gider 500 / İkinciEl 1000 = 0.50 > 0.45;
/// (b) iki araçlı sınıf: oranlar 0.50 ve 0.02 → ort 0.26; 0.50 < 0.26×1.5=0.39 DEĞİL → tetiklenir;
/// (c) km-maliyet trendi: önceki 150/300=0.50 → son 500/250=2.00 yükseliş. Adversarial-lite kenarlar:
/// İkinciEl yok → a/b tetiklenmez; tek-araç sınıf → b tetiklenmez (oran ortalamanın katı olamaz);
/// önceki-12 verisiz → c tetiklenmez. Karne kartı == filo kolonu (aynı TutSatHesap).
/// </summary>
[Collection("postgres")]
public sealed class TutSatSinyaliTests(PostgresFixture fx)
{
    private static Task ExpenseAsync(IServiceProvider sp, Guid veh, decimal net, DateTimeOffset date)
        => sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = veh, NetTutar = net, KdvOrani = 0m, Tarih = date, OdemeYontemi = PaymentMethod.Nakit });

    [Fact]
    public async Task Uc_kural_elle_oracle()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var now = DateTimeOffset.UtcNow;

        // V1: İkinciEl 1000; son-12-ay gider 500 (oran 0.50 > 0.45 → kural-a).
        // Km trendi: önceki pencerede 300 km / 150 gider (0.50) → son pencerede 250 km / 500 gider (2.00) → kural-c.
        var v1 = await veh.CreateAsync(new VehicleInput { Plaka = "34 TS 01", Grup = "EKO", IkinciElDeger = 1000m });
        await ExpenseAsync(sp, v1, 500m, now.AddMonths(-2));            // son-12 penceresi
        await ExpenseAsync(sp, v1, 150m, now.AddMonths(-18));           // önceki-12 penceresi
        // km: kiralar — önceki pencere dönüşlü 300 km; son pencere dönüşlü 250 km.
        var account = await sp.GetRequiredService<RentACar.Application.Customers.CustomerService>()
            .CreateAsync(new RentACar.Application.Customers.CustomerInput { Tip = CustomerType.Bireysel, Ad = "TS", Soyad = "C" });
        var rentals = sp.GetRequiredService<RentACar.Application.Bookings.RentalService>();
        async Task RentalKmAsync(Guid v, DateTimeOffset start, int km)
        {
            var r = await rentals.CreateDirectAsync(new RentACar.Application.Bookings.BookingInput
            { MusteriId = account, VehicleId = v, BasTar = start, BitTar = start.AddDays(2), GunlukUcret = 10m });
            await rentals.DeliverAsync(r, pickupKm: 0, pickupFuel: 8);
            await rentals.ReturnAsync(r, returnKm: km, returnFuel: 8, start.AddDays(2));
        }
        await RentalKmAsync(v1, now.AddMonths(-18), 300);               // önceki pencere
        await RentalKmAsync(v1, now.AddMonths(-2), 250);                // son pencere

        // V2 (aynı grup): İkinciEl 5000, son-12 gider 100 (oran 0.02) → sınıf ort (0.50+0.02)/2 = 0.26;
        // V1 oranı 0.50 > 0.26×1.5=0.39 → kural-b V1'de tetiklenir; V2'de hiçbiri tetiklenmez.
        var v2 = await veh.CreateAsync(new VehicleInput { Plaka = "34 TS 02", Grup = "EKO", IkinciElDeger = 5000m });
        await ExpenseAsync(sp, v2, 100m, now.AddMonths(-2));

        var rs = sp.GetRequiredService<ReportService>();
        var scorecardV1 = await rs.GetVehicleScorecardAsync(v1);
        Assert.Equal(3, scorecardV1!.TutSat.Sinyal);                        // a + b + c (elle)
        Assert.Equal(3, scorecardV1.TutSat.Gerekceler.Count);
        var scorecardV2 = await rs.GetVehicleScorecardAsync(v2);
        Assert.Equal(0, scorecardV2!.TutSat.Sinyal);

        // Filo kolonu aynı hesap + tutsat sıralaması V1'i öne alır.
        var fleet = await rs.GetFleetAnalysisAsync(sort: "tutsat");
        Assert.Equal(v1, fleet.Satirlar[0].VehicleId);
        Assert.Equal(3, fleet.Satirlar[0].TutSatSinyal);
        Assert.Equal(0, fleet.Satirlar.Single(x => x.VehicleId == v2).TutSatSinyal);
    }

    [Fact]
    public async Task Kenarlar_yanlis_pozitif_uretmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var now = DateTimeOffset.UtcNow;

        // İkinciEl YOK: yüksek gider bile a/b tetiklemez; km verisi tek pencerede → c tetiklenmez.
        var v1 = await veh.CreateAsync(new VehicleInput { Plaka = "34 TS 03", Grup = "LUX" });
        await ExpenseAsync(sp, v1, 9999m, now.AddMonths(-1));
        var rs = sp.GetRequiredService<ReportService>();
        Assert.Equal(0, (await rs.GetVehicleScorecardAsync(v1))!.TutSat.Sinyal);

        // TEK-araç sınıf: oran 0.50 > 0.45 → yalnız kural-a; kural-b tetiklenmez (ort = kendisi).
        var v2 = await veh.CreateAsync(new VehicleInput { Plaka = "34 TS 04", Grup = "TEK", IkinciElDeger = 1000m });
        await ExpenseAsync(sp, v2, 500m, now.AddMonths(-1));
        var scorecard = await rs.GetVehicleScorecardAsync(v2);
        Assert.Equal(1, scorecard!.TutSat.Sinyal);
        Assert.Contains("ikinci el değerin", scorecard.TutSat.Gerekceler.Single());
    }
}
