using RentACar.Application.Common;

namespace RentACar.Application.Bookings;

/// <summary>
/// Rezervasyon/kira doğrulama + gün/tutar hesabı.
/// NOT: Gün sayma konvansiyonu (24-saat bloğu, yukarı yuvarlama) PLACEHOLDER'dır;
/// fiyat motoru paritesi (canlı Gun_Hesapla) gelince kalibre edilecek (plan Böl. 8).
/// PR #3 fiyatı manuel günlük ücretle hesaplanır.
/// </summary>
public static class BookingMath
{
    public static void Validate(BookingInput input)
    {
        if (input.MusteriId == Guid.Empty)
            throw new ValidationException("Müşteri seçilmelidir.");
        if (input.VehicleId == Guid.Empty)
            throw new ValidationException("Araç seçilmelidir.");
        if (input.BitTar <= input.BasTar)
            throw new ValidationException("Bitiş tarihi başlangıçtan sonra olmalıdır.");
        if (input.GunlukUcret < 0)
            throw new ValidationException("Günlük ücret negatif olamaz.");
    }

    public static (int Gun, decimal Tutar) Compute(BookingInput input)
    {
        var hours = (input.BitTar - input.BasTar).TotalHours;
        var gun = Math.Max(1, (int)Math.Ceiling(hours / 24.0)); // 24-saat bloğu, yukarı yuvarla
        var tutar = gun * input.GunlukUcret;
        return (gun, tutar);
    }
}
