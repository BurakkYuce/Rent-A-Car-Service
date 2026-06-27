using RentACar.Application.Regulation;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

/// <summary>Saf vade sınıflandırma testleri (DB yok). now = 2026-06-01.</summary>
public sealed class VadeHesapTests
{
    [Theory]
    [InlineData("2026-05-25", -7, VadeBucket.Gecmis)]
    [InlineData("2026-06-01", 0, VadeBucket.YediGun)]   // bugün → ≤7
    [InlineData("2026-06-05", 4, VadeBucket.YediGun)]
    [InlineData("2026-06-08", 7, VadeBucket.YediGun)]   // sınır 7
    [InlineData("2026-06-20", 19, VadeBucket.OtuzGun)]
    [InlineData("2026-07-01", 30, VadeBucket.OtuzGun)]  // sınır 30
    [InlineData("2026-08-01", 61, VadeBucket.Ileri)]
    public void Classify_buckets(string expiryIso, int expKalan, VadeBucket expBucket)
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var expiry = DateTimeOffset.Parse(expiryIso + "T00:00:00+00:00");
        var (kalan, bucket) = VadeHesap.Classify(now, expiry);
        Assert.Equal(expKalan, kalan);
        Assert.Equal(expBucket, bucket);
    }
}
