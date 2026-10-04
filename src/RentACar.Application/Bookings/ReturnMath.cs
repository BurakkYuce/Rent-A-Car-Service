using RentACar.Domain.Entities;

namespace RentACar.Application.Bookings;

/// <summary>Dönüşte hesaplanan ek bedeller (saf hesap → birim-testli). KullanilanKm = dönüş − çıkış (gösterim).</summary>
public readonly record struct ReturnCharges(
    int FazlaKm, decimal FazlaKmBedeli,
    int EksikYakit, decimal YakitBedeli,
    int UzatmaGun, decimal UzatmaBedeli,
    decimal GenelToplam, int KullanilanKm);

/// <summary>
/// Araç dönüşü ek-bedel hesabı: fazla km, eksik yakıt, uzatma (geç dönüş).
/// SAF + deterministik. NOT: formüllerin tam parite kalibrasyonu (km limit kaynağı,
/// yuvarlama, yakıt birim modeli) fiyat motoru + canlı parite ile netleşecek; PR #4
/// mekaniği girilen parametrelerle çalışır.
/// </summary>
public static class ReturnMath
{
    public static ReturnCharges Compute(
        RentalContract c, int returnKm, int returnFuel, DateTimeOffset actualReturn, int freeKm = 0)
    {
        // Fazla km: yalnız KmLimit>0 ve çıkış km girilmişse. kmHediye = aşımdan düşülen bedava km
        // (KM Hediye — referans sistem parite). KM-aşım PARASININ TEK OTORİTESİ dönüş-zamanıdır (KURAL A);
        // fiyat motorunun create-zamanı KmAsimTutar TAHMİNİ asla para olarak persist edilmez.
        var used = c.CikisKm is int ck ? Math.Max(0, returnKm - ck) : 0;
        // #366 H1: tarife/grup kaynaklı (günlük) km hakkı GEÇ DÖNÜŞ günleriyle de büyür — müşteri faturalanan her
        // gün için o günün km hakkını alır. Elle girilmiş toplam limitte (KmLimitGunluk null) KmLimit aynen.
        var kmEntitlement = GroupKmPolicy.Entitlement(c, LateDays(c, actualReturn));
        var excessKm = 0;
        if (kmEntitlement > 0 && c.CikisKm is not null)
        {
            // LONG aritmetik: int.MaxValue hediye ile (kullanilan − limit − hediye) int'te wrap edip
            // milyarlık hayalet FazlaKm üretiyordu (adversarial BULGU 1 — deftere kadar gidiyordu).
            var excessLiters = Math.Max(0L, (long)used - kmEntitlement - Math.Max(0, freeKm));
            excessKm = (int)Math.Min(excessLiters, int.MaxValue);
        }
        // Para satırları 2 haneye yuvarlanır (satır-bazlı yuvarlama; kesirli FazlaKmUcret'te sözleşme ↔
        // fatura brütü 0,0001 ıraksıyordu — adversarial BULGU 3).
        var excessKmCharge = Math.Round(excessKm * c.FazlaKmUcret, 2, MidpointRounding.AwayFromZero);

        // Eksik yakıt: çıkış seviyesinin altına döndüyse. (Satır-bazlı 2 hane yuvarlama — BULGU 3.)
        var missingFuel = 0;
        if (c.CikisYakit is int pickupFuel)
            missingFuel = Math.Max(0, pickupFuel - returnFuel);
        var fuelCharge = Math.Round(missingFuel * c.YakitBirimUcret, 2, MidpointRounding.AwayFromZero);

        var extensionDays = LateDays(c, actualReturn);
        var extensionCharge = Math.Round(extensionDays * c.GunlukUcret, 2, MidpointRounding.AwayFromZero);

        var grandTotal = c.Tutar + excessKmCharge + fuelCharge + extensionCharge;

        return new ReturnCharges(
            excessKm, excessKmCharge, missingFuel, fuelCharge, extensionDays, extensionCharge, grandTotal, used);
    }

    /// <summary>Uzatma (geç dönüş) günü: kira gün hesabıyla AYNI kural ve AYNI 3 saat toleransı — tek kaynak
    /// BookingMath.LateReturnDays (kabul bulguları a-kkayit-09 / C-GUN). Uzatma bedeli ve km hakkı (#366 H1)
    /// AYNI gün sayısını kullanır.</summary>
    private static int LateDays(RentalContract c, DateTimeOffset actualReturn)
        => BookingMath.LateReturnDays(c.BasTar, c.BitTar, actualReturn);
}
