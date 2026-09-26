using RentACar.Application.Finance;

namespace RentACar.IntegrationTests;

/// <summary>
/// Saf KDV testleri (DB yok). Brütten net+KDV ayrıştırma; elle hesaplanmış oracle.
/// (Yuvarlama yöntemi/sırası tam parite için canlı kalibrasyon ister — bkz. KdvMath.)
/// </summary>
public sealed class KdvMathTests
{
    [Theory]
    [InlineData(1200, 0.20, 1000.00, 200.00)]  // tam bölünür
    [InlineData(700, 0.20, 583.33, 116.67)]    // yuvarlama
    [InlineData(100, 0.20, 83.33, 16.67)]
    [InlineData(0, 0.20, 0, 0)]
    public void FromGross_decomposes(decimal gross, decimal rate, decimal expNet, decimal expVat)
    {
        var (net, vat) = VatMath.FromGross(gross, rate);
        Assert.Equal(expNet, net);
        Assert.Equal(expVat, vat);
        Assert.Equal(gross, net + vat); // net + kdv == brüt (kuruş tutarlı)
    }

    [Theory]
    [InlineData(99.9999, 0.20, 100.00)]   // 4 ondalık brüt → kuruşa sabitlenir
    [InlineData(33.3349, 0.18, 33.33)]
    [InlineData(1234.5678, 0.10, 1234.57)]
    public void Net_and_kdv_are_always_two_decimals(decimal gross, decimal rate, decimal expGrossMinorUnits)
    {
        var (net, vat) = VatMath.FromGross(gross, rate);
        Assert.Equal(Math.Round(net, 2), net);
        Assert.Equal(Math.Round(vat, 2), vat);
        // net + kdv = kuruşa sabitlenmiş brüt (denge korunur). Denetim C6: beklenen ELLE sabit
        // (bağımsız oracle) — KdvMath.RoundGross'tan (üretim kodundan) türetilmez.
        Assert.Equal(expGrossMinorUnits, net + vat);
    }

    [Theory]
    [InlineData(100, 0.20, 20.00, 120.00)]  // gider: net+KDV
    [InlineData(99.99, 0.18, 18.00, 117.99)]
    [InlineData(250, 0.10, 25.00, 275.00)]
    public void FromNet_adds_kdv(decimal net, decimal rate, decimal expVat, decimal expGross)
    {
        var (vat, gross) = VatMath.FromNet(net, rate);
        Assert.Equal(expVat, vat);
        Assert.Equal(expGross, gross);
        Assert.Equal(Math.Round(vat, 2), vat); // kuruş
    }
}
