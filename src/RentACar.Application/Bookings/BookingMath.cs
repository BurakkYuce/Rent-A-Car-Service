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
        if (input.DropUcreti is < 0m)
            throw new ValidationException("Drop ücreti negatif olamaz."); // A3b-B4: crafted POST guard'ı
    }

    /// <summary>Opsiyonel metin alanı: boş → null, aksi Trim + uzunluk çiti (aşımda gürültülü red).
    /// Rezervasyon ve kira aynı alanları (Talep Türü / Proje Adı …) taşıdığından kural TEK yerde
    /// (FAZ-48; RentalService.Lim buna delege eder — iki kopya sapmasın).</summary>
    public static string? Kirp(string? s, int max, string alan)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t.Length > max)
            throw new ValidationException($"{alan} en fazla {max} karakter olabilir.");
        return t;
    }

    public static (int Gun, decimal Tutar) Compute(BookingInput input)
    {
        var gun = ComputeGun(input.BasTar, input.BitTar);
        var tutar = gun * input.GunlukUcret;
        return (gun, tutar);
    }

    /// <summary>
    /// Kısmi dönem gün eşiği (saat): kalan saat bunu <b>bulursa</b> +1 gün.
    ///
    /// <para><b>Değer artık tahmin değil, canlının kaynağından ölçüldü</b> (2026-08-06 parite koşusu).
    /// TürevRent'in `kiralama.aspx` sayfasında gün hesabını yapan fonksiyon `Hizmet_Gun_Bul` (19 çağrı;
    /// eski `Gun_Hesapla` yalnız 2 yerde kalmış) ve kalan süreyi `calculateTimeDifference` ile
    /// <b>dakika duyarlı</b> hesaplayıp <c>Saat_Farki_Hesap</c> ile karşılaştırıyor; o alanın canlıdaki
    /// değeri <b>3</b>. Eski 2.9, fonksiyonun yalnız AYNI-GÜN dalında uygulanan <c>+0.1</c> toleransından
    /// türetilmiş bir yaklaşımdı; o dal sonucu zaten hep 1 güne sabitlediği için 2.9 pratikte sadece
    /// [2.9, 3.0) aralığında (6 dakikalık pencere) <b>bize bir gün fazla faturalatıyordu</b>.</para>
    ///
    /// Uzatma/geç-dönüş bu kuralı KULLANMAZ (ReturnMath ayrı: ceil).
    /// </summary>
    public const double KismiGunEsigiSaat = 3.0;

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
