using RentACar.Application.Regulation;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

/// <summary>Saf vade sınıflandırma testleri (DB yok). now = 2026-06-01.</summary>
public sealed class VadeHesapTests
{
    [Theory]
    [InlineData("2026-05-25", -7, DueBucket.Gecmis)]
    [InlineData("2026-06-01", 0, DueBucket.YediGun)]   // bugün → ≤7
    [InlineData("2026-06-05", 4, DueBucket.YediGun)]
    [InlineData("2026-06-08", 7, DueBucket.YediGun)]   // sınır 7
    [InlineData("2026-06-20", 19, DueBucket.OtuzGun)]
    [InlineData("2026-07-01", 30, DueBucket.OtuzGun)]  // sınır 30
    [InlineData("2026-08-01", 61, DueBucket.Ileri)]
    public void Classify_buckets(string expiryIso, int expRemaining, DueBucket expBucket)
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var expiry = DateTimeOffset.Parse(expiryIso + "T00:00:00+00:00");
        var (remaining, bucket) = DueCalculation.Classify(now, expiry);
        Assert.Equal(expRemaining, remaining);
        Assert.Equal(expBucket, bucket);
    }
}
