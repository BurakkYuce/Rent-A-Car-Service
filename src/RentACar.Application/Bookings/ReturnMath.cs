using RentACar.Domain.Entities;

namespace RentACar.Application.Bookings;

/// <summary>Dönüşte hesaplanan ek bedeller (saf hesap → birim-testli).</summary>
public readonly record struct ReturnCharges(
    int FazlaKm, decimal FazlaKmBedeli,
    int EksikYakit, decimal YakitBedeli,
    int UzatmaGun, decimal UzatmaBedeli,
    decimal GenelToplam);

/// <summary>
/// Araç dönüşü ek-bedel hesabı: fazla km, eksik yakıt, uzatma (geç dönüş).
/// SAF + deterministik. NOT: formüllerin tam parite kalibrasyonu (km limit kaynağı,
/// yuvarlama, yakıt birim modeli) fiyat motoru + canlı parite ile netleşecek; PR #4
/// mekaniği girilen parametrelerle çalışır.
/// </summary>
public static class ReturnMath
{
    public static ReturnCharges Compute(RentalContract c, int donusKm, int donusYakit, DateTimeOffset gercekDonus)
    {
        // Fazla km: yalnız KmLimit>0 ve çıkış km girilmişse.
        var fazlaKm = 0;
        if (c.KmLimit > 0 && c.CikisKm is int cikisKm)
        {
            var katEdilen = donusKm - cikisKm;
            fazlaKm = Math.Max(0, katEdilen - c.KmLimit);
        }
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
            fazlaKm, fazlaKmBedeli, eksikYakit, yakitBedeli, uzatmaGun, uzatmaBedeli, genelToplam);
    }
}
