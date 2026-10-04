using RentACar.Application.Bookings;
using RentACar.Domain.Entities;

namespace RentACar.IntegrationTests;

/// <summary>
/// Saf birim testleri (DB yok). Beklenen değerler elle hesaplanmıştır (formül spec'i =
/// bağımsız oracle; aritmetik tek anlamlı).
/// </summary>
public sealed class ReturnMathTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Bit = new(2026, 7, 5, 9, 0, 0, TimeSpan.Zero); // 4 gün

    private static RentalContract Base() => new()
    {
        BasTar = Start, BitTar = Bit, Gun = 4, GunlukUcret = 100m, Tutar = 400m,
        CikisKm = 1000, CikisYakit = 8
    };

    [Fact]
    public void No_extras_genel_toplam_equals_base()
    {
        var c = Base();
        var r = ReturnMath.Compute(c, returnKm: 1300, returnFuel: 8, actualReturn: Bit);
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
        var r = ReturnMath.Compute(c, returnKm: 1500, returnFuel: 8, actualReturn: Bit);
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
        var r = ReturnMath.Compute(c, returnKm: 1100, returnFuel: 5, actualReturn: Bit);
        Assert.Equal(3, r.EksikYakit);
        Assert.Equal(150m, r.YakitBedeli);
        Assert.Equal(550m, r.GenelToplam); // 400 + 150
    }

    [Fact]
    public void Late_return_charges_extension()
    {
        var c = Base();
        // 2 gün geç (48 saat) → uzatma 2 gün × 100 = 200
        var r = ReturnMath.Compute(c, returnKm: 1100, returnFuel: 8, actualReturn: Bit.AddDays(2));
        Assert.Equal(2, r.UzatmaGun);
        Assert.Equal(200m, r.UzatmaBedeli);
        Assert.Equal(600m, r.GenelToplam);
    }

    /// <summary>
    /// Kabul bulguları a-kkayit-09 / C-GUN: geç dönüş kira gün hesabıyla AYNI 3 saat toleransını kullanır.
    /// Eskiden 1 dakika (hatta 0,48 sn) gecikme tam gün faturalanıyordu. ELLE ORACLE (kira 4 gün × 100):
    /// kısmi gecikme 3 saatten AZSA ücretsiz, 3 saati BULURSA +1 gün; tam 24 saat blokları ayrıca sayılır.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0, 480, 0)]     // 0,48 sn geç → tolerans içinde
    [InlineData(0, 0, 1, 0, 0)]       // 1 dk geç → tolerans içinde
    [InlineData(0, 2, 59, 0, 0)]      // 2 sa 59 dk geç → tolerans içinde
    [InlineData(0, 3, 0, 0, 1)]       // tam 3 sa geç → +1 gün
    [InlineData(1, 2, 0, 0, 1)]       // 1 gün 2 sa geç → yalnız tam gün
    [InlineData(1, 3, 0, 0, 2)]       // 1 gün 3 sa geç → tam gün + kısmi
    public void Late_return_uses_rental_day_tolerance(int days, int hours, int minutes, int millis, int expectedDays)
    {
        var c = Base();
        var late = Bit.AddDays(days).AddHours(hours).AddMinutes(minutes).AddMilliseconds(millis);
        var r = ReturnMath.Compute(c, returnKm: 1100, returnFuel: 8, actualReturn: late);
        Assert.Equal(expectedDays, r.UzatmaGun);
        Assert.Equal(expectedDays * 100m, r.UzatmaBedeli);
        Assert.Equal(400m + expectedDays * 100m, r.GenelToplam);
    }

    /// <summary>
    /// Tolerans TOPLAM kira süresine bir kez uygulanır (kira gün hesabıyla aynı): planlanan 4 gün 2 saat
    /// (kısmi 2 sa → 4 gün faturalanmış) iken 1 saat geç dönüş toplam 4 gün 3 saat = 5 gün → +1 gün.
    /// Gecikmeye ayrı 3 saat daha tanımak toleransı iki kez kullandırırdı.
    /// </summary>
    [Fact]
    public void Tolerance_is_shared_with_the_planned_period()
    {
        var c = Base();
        c.BitTar = Bit.AddHours(2);
        var r = ReturnMath.Compute(c, returnKm: 1100, returnFuel: 8, actualReturn: Bit.AddHours(3));
        Assert.Equal(1, r.UzatmaGun);
        Assert.Equal(100m, r.UzatmaBedeli);
    }

    /// <summary>
    /// #372 adversarial L1 (probe P2): geç dönüş günü SÖZLEŞMEDE FATURALANAN güne (c.Gun) göre sayılır, planlanan
    /// süre yeniden hesaplanmaz. ELLE ORACLE: planlanan 2 gün 2 sa 57 dk (kural 2 gün derdi) ama sözleşme 3 gün
    /// faturalanmış; 5 dk geç dönüş → toplam 2 gün 3 sa 02 dk = 3 gün → 3 − 3 = 0 ek gün (eski kural 3 − 2 = 1 gün
    /// daha faturalıyordu — müşteri aynı günü iki kez ödüyordu).
    /// </summary>
    [Fact]
    public void Late_days_are_counted_against_billed_days()
    {
        var c = Base();
        c.Gun = 3;
        c.Tutar = 300m;
        c.BitTar = Start.AddDays(2).AddHours(2).AddMinutes(57);
        var r = ReturnMath.Compute(c, returnKm: 1100, returnFuel: 8, actualReturn: c.BitTar.AddMinutes(5));
        Assert.Equal(0, r.UzatmaGun);
        Assert.Equal(0m, r.UzatmaBedeli);
        Assert.Equal(300m, r.GenelToplam);
    }

    [Fact]
    public void Combined_charges_sum()
    {
        var c = Base();
        c.KmLimit = 400; c.FazlaKmUcret = 2m; c.YakitBirimUcret = 50m;
        // fazla km: 1500-1000-400=100 → 200; yakıt: 8-6=2 → 100; uzatma: 1 gün → 100
        var r = ReturnMath.Compute(c, returnKm: 1500, returnFuel: 6, actualReturn: Bit.AddDays(1));
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
        var r = ReturnMath.Compute(c, returnKm: 9999, returnFuel: 8, actualReturn: Bit);
        Assert.Equal(0, r.FazlaKm);
        Assert.Equal(0m, r.FazlaKmBedeli);
    }
}
