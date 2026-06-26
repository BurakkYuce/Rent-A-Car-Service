using RentACar.Application.Bookings;
using RentACar.Domain.Entities;

namespace RentACar.IntegrationTests;

/// <summary>
/// Saf birim testleri (DB yok). Beklenen değerler elle hesaplanmıştır (formül spec'i =
/// bağımsız oracle; aritmetik tek anlamlı).
/// </summary>
public sealed class ReturnMathTests
{
    private static readonly DateTimeOffset Bas = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Bit = new(2026, 7, 5, 9, 0, 0, TimeSpan.Zero); // 4 gün

    private static RentalContract Base() => new()
    {
        BasTar = Bas, BitTar = Bit, Gun = 4, GunlukUcret = 100m, Tutar = 400m,
        CikisKm = 1000, CikisYakit = 8
    };

    [Fact]
    public void No_extras_genel_toplam_equals_base()
    {
        var c = Base();
        var r = ReturnMath.Compute(c, donusKm: 1300, donusYakit: 8, gercekDonus: Bit);
        Assert.Equal(0, r.FazlaKm);
        Assert.Equal(0m, r.FazlaKmBedeli);
        Assert.Equal(0, r.EksikYakit);
        Assert.Equal(0, r.UzatmaGun);
        Assert.Equal(400m, r.GenelToplam);
    }

    [Fact]
    public void Excess_km_charged()
    {
        var c = Base();
        c.KmLimit = 400;          // 4 gün × 100 km gibi (manuel)
        c.FazlaKmUcret = 2m;
        // katEdilen = 1500-1000 = 500; fazla = 500-400 = 100; bedel = 200
        var r = ReturnMath.Compute(c, donusKm: 1500, donusYakit: 8, gercekDonus: Bit);
        Assert.Equal(100, r.FazlaKm);
        Assert.Equal(200m, r.FazlaKmBedeli);
        Assert.Equal(600m, r.GenelToplam); // 400 + 200
    }

    [Fact]
    public void Fuel_deficit_charged()
    {
        var c = Base();
        c.YakitBirimUcret = 50m;
        // eksik = 8 - 5 = 3; bedel = 150
        var r = ReturnMath.Compute(c, donusKm: 1100, donusYakit: 5, gercekDonus: Bit);
        Assert.Equal(3, r.EksikYakit);
        Assert.Equal(150m, r.YakitBedeli);
        Assert.Equal(550m, r.GenelToplam); // 400 + 150
    }

    [Fact]
    public void Late_return_charges_extension()
    {
        var c = Base();
        // 2 gün geç (48 saat) → uzatma 2 gün × 100 = 200
        var r = ReturnMath.Compute(c, donusKm: 1100, donusYakit: 8, gercekDonus: Bit.AddDays(2));
        Assert.Equal(2, r.UzatmaGun);
        Assert.Equal(200m, r.UzatmaBedeli);
        Assert.Equal(600m, r.GenelToplam);
    }

    [Fact]
    public void Partial_day_late_rounds_up()
    {
        var c = Base();
        // 1 saat geç → 1 güne yuvarlanır
        var r = ReturnMath.Compute(c, donusKm: 1100, donusYakit: 8, gercekDonus: Bit.AddHours(1));
        Assert.Equal(1, r.UzatmaGun);
        Assert.Equal(100m, r.UzatmaBedeli);
    }

    [Fact]
    public void Combined_charges_sum()
    {
        var c = Base();
        c.KmLimit = 400; c.FazlaKmUcret = 2m; c.YakitBirimUcret = 50m;
        // fazla km: 1500-1000-400=100 → 200; yakıt: 8-6=2 → 100; uzatma: 1 gün → 100
        var r = ReturnMath.Compute(c, donusKm: 1500, donusYakit: 6, gercekDonus: Bit.AddDays(1));
        Assert.Equal(200m, r.FazlaKmBedeli);
        Assert.Equal(100m, r.YakitBedeli);
        Assert.Equal(100m, r.UzatmaBedeli);
        Assert.Equal(800m, r.GenelToplam); // 400 + 200 + 100 + 100
    }

    [Fact]
    public void Km_limit_zero_means_unlimited()
    {
        var c = Base(); // KmLimit=0
        c.FazlaKmUcret = 5m;
        var r = ReturnMath.Compute(c, donusKm: 9999, donusYakit: 8, gercekDonus: Bit);
        Assert.Equal(0, r.FazlaKm);
        Assert.Equal(0m, r.FazlaKmBedeli);
    }
}
