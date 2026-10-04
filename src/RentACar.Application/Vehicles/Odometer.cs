using RentACar.Application.Common;

namespace RentACar.Application.Vehicles;

/// <summary>
/// Araç odometresinin TEK kuralı: elle girilen km geriye gidemez. Araç kartı (<see cref="VehicleService.UpdateAsync(Guid, VehicleInput, string?, CancellationToken)"/>)
/// ve manuel km girişi (<c>VehicleRepository.AddManualKmAsync</c>) aynı metodu çağırır — iki kopya zamanla ayrışırdı
/// (kabul bulgusu: kart 15.500 → 9.000'e indirebiliyordu, sonra manuel giriş kuralı 9.000'e göre işliyordu).
/// Otomatik okumalar (kira teslim/dönüş, servis tamamlama) reddetmez, yalnız <see cref="Advance"/> ile ileri taşır.
/// </summary>
public static class Odometer
{
    /// <summary>Elle girilen km mevcut odometreden küçükse reddeder (alan <c>km</c>).</summary>
    public static void EnsureNotBackwards(int current, int requested)
    {
        if (requested < current)
            throw new ValidationException($"KM geriye gidemez (araç odometresi {current}).", "km");
    }

    /// <summary>Otomatik okuma: araç km'si yalnız ileri gider (küçük okuma odometreyi değiştirmez).</summary>
    public static int Advance(int current, int reading) => Math.Max(current, reading);
}
