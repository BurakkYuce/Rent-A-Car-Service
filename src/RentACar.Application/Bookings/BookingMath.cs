using RentACar.Application.Common;

namespace RentACar.Application.Bookings;

/// <summary>
/// Rezervasyon/kira doğrulama + gün/tutar hesabı.
/// GÜN KURALI (canlı referans sistem kalibrasyonu, 2026-07-07): 24-saat TAM blok + kısmi dönem eşiği (~3 saat)
/// aşarsa +1; en az 1. Eski 24h-YUKARI-YUVARLAMA (ceil) kısa taşmalı kirayı (25 saat → 2 gün) 1 gün FAZLA
/// ücretlendiriyordu; floor+eşik referans sistem ile hizalar (25 saat → 1 gün; 28 saat → 2 gün).
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
    public static string? Clamp(string? s, int max, string alan)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t.Length > max)
            throw new ValidationException($"{alan} en fazla {max} karakter olabilir.");
        return t;
    }

    public static (int Gun, decimal Tutar) Compute(BookingInput input)
    {
        var day = ComputeDays(input.BasTar, input.BitTar);
        var amount = day * input.GunlukUcret;
        return (day, amount);
    }

    /// <summary>
    /// Kısmi dönem gün eşiği (saat): kalan saat bunu <b>bulursa</b> +1 gün.
    ///
    /// <para><b>Değer artık tahmin değil, canlının kaynağından ölçüldü</b> (2026-08-06 parite koşusu).
    /// referans sistem'in `kiralama.aspx` sayfasında gün hesabını yapan fonksiyon `Hizmet_Gun_Bul` (19 çağrı;
    /// eski `Gun_Hesapla` yalnız 2 yerde kalmış) ve kalan süreyi `calculateTimeDifference` ile
    /// <b>dakika duyarlı</b> hesaplayıp <c>Saat_Farki_Hesap</c> ile karşılaştırıyor; o alanın canlıdaki
    /// değeri <b>3</b>. Eski 2.9, fonksiyonun yalnız AYNI-GÜN dalında uygulanan <c>+0.1</c> toleransından
    /// türetilmiş bir yaklaşımdı; o dal sonucu zaten hep 1 güne sabitlediği için 2.9 pratikte sadece
    /// [2.9, 3.0) aralığında (6 dakikalık pencere) <b>bize bir gün fazla faturalatıyordu</b>.</para>
    ///
    /// Geç dönüş de AYNI kuralı kullanır (<see cref="LateReturnDays"/>; kabul bulguları a-kkayit-09 / C-GUN —
    /// eskiden ReturnMath ayrı bir "ceil" kuralıyla 1 dakikalık gecikmeyi tam gün faturalatıyordu).
    /// <c>TenantSettings.SaatFarkiToleransDk</c> hâlâ BEKLEMEDE: eşik firma ayarına bağlanırsa kira, fiyat
    /// motoru ve dönüş birlikte değişmeli — bu sabit tek kaynak olduğu için bağlama tek noktadan yapılır.
    /// </summary>
    public const double PartialDayThresholdHours = 3.0;

    /// <summary>
    /// Geç dönüşte faturalanacak EK gün: gerçek dönüşe kadarki toplam süre <see cref="ComputeDays"/> ile
    /// sayılır, sözleşmede ZATEN FATURALANAN gün (<paramref name="billedDays"/> = RentalContract.Gun) düşülür.
    /// Tolerans böylece TOPLAM kira süresine BİR KEZ uygulanır (kira gün hesabıyla birebir): 3 günlük kirada
    /// 2 sa 59 dk gecikme ücretsiz, 3 saat +1 gün. Planlanan bitişte ya da öncesinde dönüş 0.
    ///
    /// <para>#372 adversarial L1: planlanan süre YENİDEN hesaplanmaz — sözleşme günü kuraldan farklıysa (elle/
    /// eski kayıt: 2 gün 2 sa 57 dk ama 3 gün faturalanmış) yeniden hesap 2 der ve 5 dk gecikmede müşteriye
    /// zaten ödediği günü bir kez daha faturalardı.</para>
    /// </summary>
    public static int LateReturnDays(
        DateTimeOffset start, int billedDays, DateTimeOffset plannedEnd, DateTimeOffset actualReturn)
        => actualReturn <= plannedEnd
            ? 0
            : Math.Max(0, ComputeDays(start, actualReturn) - billedDays);

    /// <summary>Gün sayısı: 24-saat TAM blok (floor) + kısmi dönem <see cref="PartialDayThresholdHours"/>'ı aşarsa
    /// +1; en az 1. Fiyat motoru + kira/rezervasyon/teklif/uzatma-gün'ü kullanır (referans sistem parite).</summary>
    public static int ComputeDays(DateTimeOffset start, DateTimeOffset bit)
    {
        var hour = (bit - start).TotalHours;
        if (hour <= 0) return 1; // Validate zaten bit>bas zorlar; savunma.
        var fullDays = (int)Math.Floor(hour / 24.0);
        var partialHours = hour - fullDays * 24.0;
        return Math.Max(1, fullDays + (partialHours >= PartialDayThresholdHours ? 1 : 0));
    }
}
