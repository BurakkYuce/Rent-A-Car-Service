using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Expenses;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ2-2.4 — Kalıntı projeksiyonu (azalan bakiye) + filo yenileme-bütçesi özet kartı.
/// BAĞIMSIZ ORACLE (elle): Alım 1000, İkinciEl 810, yaş 2 yıl → yıllık oran (810/1000)^(1/2)=0.90
/// (gözlenen) → 12 ay sonra 810×0.90=729.00, 24 ay 729×0.90=656.10. Yaş&lt;1 → varsayılan 0.85:
/// İkinciEl 500 → 425.00/361.25. İkinciEl yoksa projeksiyon YOK (uydurma taban yok). Değer ARTIŞI
/// projekte edilmez (oran 1.00 tavan). Filo kartı: sinyal ≥2 aday + adayların Deger12Ay toplamı.
/// </summary>
[Collection("postgres")]
public sealed class KalintiProjeksiyonTests(PostgresFixture fx)
{
    [Fact]
    public async Task Gozlenen_oran_ve_varsayilan_elle_oracle()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var rs = sp.GetRequiredService<ReportService>();
        var now = DateTimeOffset.UtcNow;

        // Gözlenen oran: (810/1000)^(1/2) = 0.90 → 729.00 / 656.10 (elle).
        var v1 = await veh.CreateAsync(new VehicleInput
        { Plaka = "34 KL 01", AlimBedeli = 1000m, IkinciElDeger = 810m, AlimTarihi = now.AddYears(-2) });
        var k1 = (await rs.GetVehicleScorecardAsync(v1))!.Kalinti!;
        Assert.Equal(0.90m, k1.YillikOran);
        Assert.True(k1.OranGozlenen);
        Assert.Equal(729.00m, k1.Deger12Ay);
        Assert.Equal(656.10m, k1.Deger24Ay);

        // Yaş < 1 yıl → varsayılan 0.85: 500×0.85=425.00, 425×0.85=361.25 (elle).
        var v2 = await veh.CreateAsync(new VehicleInput
        { Plaka = "34 KL 02", AlimBedeli = 1000m, IkinciElDeger = 500m, AlimTarihi = now.AddMonths(-6) });
        var k2 = (await rs.GetVehicleScorecardAsync(v2))!.Kalinti!;
        Assert.Equal(0.85m, k2.YillikOran);
        Assert.False(k2.OranGozlenen);
        Assert.Equal(425.00m, k2.Deger12Ay);
        Assert.Equal(361.25m, k2.Deger24Ay);

        // İkinciEl yok → projeksiyon yok (null) — uydurma taban basılmaz.
        var v3 = await veh.CreateAsync(new VehicleInput { Plaka = "34 KL 03", AlimBedeli = 1000m });
        Assert.Null((await rs.GetVehicleScorecardAsync(v3))!.Kalinti);
    }

    [Fact]
    public void Deger_artisi_projekte_edilmez_ve_kenarlar() // saf helper — kenar matrisi
    {
        var now = DateTimeOffset.UtcNow;

        // İkinciEl > Alım (enflasyonist piyasa): gözlenen oran >1 çıkar → 1.00'e SINIRLANIR (düz çizgi).
        var increase = ResidualProjection.Calculate(1000m, 1200m, now.AddYears(-2), now)!;
        Assert.Equal(1.00m, increase.YillikOran);
        Assert.Equal(1200.00m, increase.Deger12Ay);
        Assert.Equal(1200.00m, increase.Deger24Ay);

        // Alım tarihi yok → yaş bilinmiyor → varsayılan 0.85 (gözlenen değil).
        var undated = ResidualProjection.Calculate(1000m, 800m, null, now)!;
        Assert.Equal(0.85m, undated.YillikOran);
        Assert.False(undated.OranGozlenen);

        // Alım bedeli yok → oran türetilemez → varsayılan; İkinciEl tabanıyla projeksiyon sürer.
        var withoutPurchase = ResidualProjection.Calculate(null, 400m, now.AddYears(-3), now)!;
        Assert.Equal(0.85m, withoutPurchase.YillikOran);
        Assert.Equal(340.00m, withoutPurchase.Deger12Ay);

        // İkinciEl 0/negatif/yok → null.
        Assert.Null(ResidualProjection.Calculate(1000m, 0m, now.AddYears(-2), now));
        Assert.Null(ResidualProjection.Calculate(1000m, null, now.AddYears(-2), now));
    }

    [Fact]
    public async Task Filo_yenileme_butcesi_karti()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var now = DateTimeOffset.UtcNow;

        // 2.2 reçetesi: V1 sinyal 2 (kural a: 500/1000=0.50>0.45 + kural b: sınıf ort (0.50+0.02)/2=0.26,
        // 0.50>0.39). AlimTarihi YOK → projeksiyon varsayılan 0.85 → Deger12Ay = 1000×0.85 = 850.00 (elle).
        var v1 = await veh.CreateAsync(new VehicleInput { Plaka = "34 KL 04", Grup = "EKO", IkinciElDeger = 1000m });
        var v2 = await veh.CreateAsync(new VehicleInput { Plaka = "34 KL 05", Grup = "EKO", IkinciElDeger = 5000m });
        var expenses = sp.GetRequiredService<ExpenseService>();
        Task Expense(Guid v, decimal net) => expenses.CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Arac, VehicleId = v, NetTutar = net, KdvOrani = 0m,
            Tarih = now.AddMonths(-2), OdemeYontemi = PaymentMethod.Nakit
        });
        await Expense(v1, 500m);
        await Expense(v2, 100m);

        var fleet = await sp.GetRequiredService<ReportService>().GetFleetAnalysisAsync();
        Assert.Equal(2, fleet.Satirlar.Single(x => x.VehicleId == v1).TutSatSinyal);
        var candidate = fleet.TutSatAday!;
        Assert.Equal(1, candidate.AracSayisi);                    // yalnız V1 (V2 sinyal 0)
        Assert.Equal(850.00m, candidate.TahminiGeriKazanim12Ay);  // 1000×0.85 (elle)
    }
}
