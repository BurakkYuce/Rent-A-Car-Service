namespace RentACar.Application.Finance;

/// <summary>
/// KDV hesabı. Türk araç kiralama fiyatları KDV-DAHİL (brüt) varsayılır → brütten net+KDV
/// ayrıştırılır: net = round(brüt/(1+oran), 2), KDV = brüt − net (kuruş tutarlılığı için).
///
/// NOT (plan Böl. 8): yuvarlama yöntemi/sırası (AwayFromZero, satır mı toplam mı) kuruş
/// tutarını belirler ve TAM PARİTE için canlı TürevRent çıktısıyla kalibre edilmelidir.
/// PR #7 makul bir varsayılan uygular; kalibrasyon fiyat motoru/parite ile gelecek.
/// </summary>
public static class KdvMath
{
    public static (decimal Net, decimal Kdv) FromGross(decimal gross, decimal kdvRate)
    {
        if (kdvRate < 0) throw new ArgumentOutOfRangeException(nameof(kdvRate));
        var net = Math.Round(gross / (1 + kdvRate), 2, MidpointRounding.AwayFromZero);
        var kdv = gross - net;
        return (net, kdv);
    }
}
