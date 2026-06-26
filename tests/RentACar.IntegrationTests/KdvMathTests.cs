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
    public void FromGross_decomposes(decimal gross, decimal rate, decimal expNet, decimal expKdv)
    {
        var (net, kdv) = KdvMath.FromGross(gross, rate);
        Assert.Equal(expNet, net);
        Assert.Equal(expKdv, kdv);
        Assert.Equal(gross, net + kdv); // net + kdv == brüt (kuruş tutarlı)
    }
}
