using RentACar.Domain.Entities;

namespace RentACar.Application.Bookings;

/// <summary>
/// Kira tutar yeniden-hesabı (tek doğruluk kaynağı). GenelToplam = baz kira brütü (Tutar +
/// dönüş ek bedelleri) + ek hizmet kalemleri brütü. Hem ek hizmet ekleme/çıkarma hem dönüş
/// aynı formülü kullanır → tutarlılık. Tüm parçalar brüt (KDV-dahil); KDV faturada ayrıştırılır.
/// </summary>
public static class RentalTotals
{
    /// <summary>Ek hizmet HARİÇ baz kira brütü: Tutar + fazla km + eksik yakıt + uzatma.</summary>
    public static decimal BaseGross(RentalContract c)
        => c.Tutar + c.FazlaKmBedeli + c.YakitBedeli + c.UzatmaBedeli;

    /// <summary>GenelToplam + Bakiye'yi baz brüt + ek hizmet brütü toplamına göre günceller.</summary>
    public static void Recompute(RentalContract c, decimal ekHizmetToplam)
    {
        c.GenelToplam = BaseGross(c) + ekHizmetToplam;
        c.Bakiye = c.GenelToplam - c.Tahsilat;
    }
}
