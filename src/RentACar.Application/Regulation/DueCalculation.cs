using RentACar.Domain.Enums;

namespace RentACar.Application.Regulation;

/// <summary>
/// Vade (bitiş) sınıflandırması — SAF + deterministik (birim-testli). Kalan gün = takvim
/// günü farkı; kova: geçmiş / ≤7 / ≤30 / ileri.
/// </summary>
public static class DueCalculation
{
    public static (int KalanGun, DueBucket Bucket) Classify(DateTimeOffset now, DateTimeOffset expiry)
    {
        var remaining = (expiry.UtcDateTime.Date - now.UtcDateTime.Date).Days;
        var bucket = remaining < 0 ? DueBucket.Gecmis
            : remaining <= 7 ? DueBucket.YediGun
            : remaining <= 30 ? DueBucket.OtuzGun
            : DueBucket.Ileri;
        return (remaining, bucket);
    }
}
