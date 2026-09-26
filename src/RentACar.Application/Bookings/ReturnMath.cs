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
        var excessKm = 0;
        if (c.KmLimit > 0 && c.CikisKm is not null)
        {
            // LONG aritmetik: int.MaxValue hediye ile (kullanilan − limit − hediye) int'te wrap edip
            // milyarlık hayalet FazlaKm üretiyordu (adversarial BULGU 1 — deftere kadar gidiyordu).
            var excessLiters = Math.Max(0L, (long)used - c.KmLimit - Math.Max(0, freeKm));
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

        // Uzatma: planlanan bitişten sonra döndüyse (24-saat bloğu, yukarı yuvarla).
        var extensionDays = 0;
        if (actualReturn > c.BitTar)
            extensionDays = Math.Max(1, (int)Math.Ceiling((actualReturn - c.BitTar).TotalHours / 24.0));
        var extensionCharge = Math.Round(extensionDays * c.GunlukUcret, 2, MidpointRounding.AwayFromZero);

        var grandTotal = c.Tutar + excessKmCharge + fuelCharge + extensionCharge;

        return new ReturnCharges(
            excessKm, excessKmCharge, missingFuel, fuelCharge, extensionDays, extensionCharge, grandTotal, used);
    }
}
