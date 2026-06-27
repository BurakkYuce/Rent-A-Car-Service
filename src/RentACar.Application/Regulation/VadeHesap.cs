using RentACar.Domain.Enums;

namespace RentACar.Application.Regulation;

/// <summary>
/// Vade (bitiş) sınıflandırması — SAF + deterministik (birim-testli). Kalan gün = takvim
/// günü farkı; kova: geçmiş / ≤7 / ≤30 / ileri.
/// </summary>
public static class VadeHesap
{
    public static (int KalanGun, VadeBucket Bucket) Classify(DateTimeOffset now, DateTimeOffset expiry)
    {
        var kalan = (expiry.UtcDateTime.Date - now.UtcDateTime.Date).Days;
        var bucket = kalan < 0 ? VadeBucket.Gecmis
            : kalan <= 7 ? VadeBucket.YediGun
            : kalan <= 30 ? VadeBucket.OtuzGun
            : VadeBucket.Ileri;
        return (kalan, bucket);
    }
}
