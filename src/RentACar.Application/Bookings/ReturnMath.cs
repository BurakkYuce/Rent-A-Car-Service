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
        RentalContract c, int donusKm, int donusYakit, DateTimeOffset gercekDonus, int kmHediye = 0)
    {
        // Fazla km: yalnız KmLimit>0 ve çıkış km girilmişse. kmHediye = aşımdan düşülen bedava km
        // (KM Hediye — TürevRent parite). KM-aşım PARASININ TEK OTORİTESİ dönüş-zamanıdır (KURAL A);
        // fiyat motorunun create-zamanı KmAsimTutar TAHMİNİ asla para olarak persist edilmez.
        var kullanilan = c.CikisKm is int ck ? Math.Max(0, donusKm - ck) : 0;
        var fazlaKm = 0;
        if (c.KmLimit > 0 && c.CikisKm is not null)
            fazlaKm = Math.Max(0, kullanilan - c.KmLimit - Math.Max(0, kmHediye));
        var fazlaKmBedeli = fazlaKm * c.FazlaKmUcret;

        // Eksik yakıt: çıkış seviyesinin altına döndüyse.
        var eksikYakit = 0;
        if (c.CikisYakit is int cikisYakit)
            eksikYakit = Math.Max(0, cikisYakit - donusYakit);
        var yakitBedeli = eksikYakit * c.YakitBirimUcret;

        // Uzatma: planlanan bitişten sonra döndüyse (24-saat bloğu, yukarı yuvarla).
        var uzatmaGun = 0;
        if (gercekDonus > c.BitTar)
            uzatmaGun = Math.Max(1, (int)Math.Ceiling((gercekDonus - c.BitTar).TotalHours / 24.0));
        var uzatmaBedeli = uzatmaGun * c.GunlukUcret;

        var genelToplam = c.Tutar + fazlaKmBedeli + yakitBedeli + uzatmaBedeli;

        return new ReturnCharges(
            fazlaKm, fazlaKmBedeli, eksikYakit, yakitBedeli, uzatmaGun, uzatmaBedeli, genelToplam, kullanilan);
    }
}
