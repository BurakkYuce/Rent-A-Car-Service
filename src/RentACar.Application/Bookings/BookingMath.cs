using RentACar.Application.Common;

namespace RentACar.Application.Bookings;

/// <summary>
/// Rezervasyon/kira doğrulama + gün/tutar hesabı.
/// GÜN KURALI (canlı TürevRent kalibrasyonu, 2026-07-07): 24-saat TAM blok + kısmi dönem eşiği (~3 saat)
/// aşarsa +1; en az 1. Eski 24h-YUKARI-YUVARLAMA (ceil) kısa taşmalı kirayı (25 saat → 2 gün) 1 gün FAZLA
/// ücretlendiriyordu; floor+eşik TürevRent ile hizalar (25 saat → 1 gün; 28 saat → 2 gün).
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
        var gun = ComputeGun(input.BasTar, input.BitTar);
        var tutar = gun * input.GunlukUcret;
        return (gun, tutar);
    }

    /// <summary>Kısmi dönem gün eşiği (saat): kalan saat bunu aşarsa +1 gün. Canlı TürevRent
    /// Saat_Farki_Hesap=3 + ~0.1sa grace → efektif ~2.9sa (kalibrasyon; sınır değeri yaklaşık, ince
    /// ayar gerekirse bu sabit değişir). Uzatma/geç-dönüş bu kuralı KULLANMAZ (ReturnMath ayrı: ceil).</summary>
    public const double KismiGunEsigiSaat = 2.9;

    /// <summary>Gün sayısı: 24-saat TAM blok (floor) + kısmi dönem <see cref="KismiGunEsigiSaat"/>'ı aşarsa
    /// +1; en az 1. Fiyat motoru + kira/rezervasyon/teklif/uzatma-gün'ü kullanır (TürevRent parite).</summary>
    public static int ComputeGun(DateTimeOffset bas, DateTimeOffset bit)
    {
        var saat = (bit - bas).TotalHours;
        if (saat <= 0) return 1; // Validate zaten bit>bas zorlar; savunma.
        var tamGun = (int)Math.Floor(saat / 24.0);
        var kismiSaat = saat - tamGun * 24.0;
        return Math.Max(1, tamGun + (kismiSaat >= KismiGunEsigiSaat ? 1 : 0));
    }
}
