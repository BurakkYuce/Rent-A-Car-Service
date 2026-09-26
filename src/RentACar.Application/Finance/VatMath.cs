namespace RentACar.Application.Finance;

/// <summary>
/// KDV hesabı. Türk araç kiralama fiyatları KDV-DAHİL (brüt) varsayılır → brütten net+KDV
/// ayrıştırılır: net = round(brüt/(1+oran), 2), KDV = brüt − net (kuruş tutarlılığı için).
///
/// NOT (plan Böl. 8): yuvarlama yöntemi/sırası (AwayFromZero, satır mı toplam mı) kuruş
/// tutarını belirler ve TAM PARİTE için canlı referans sistem çıktısıyla kalibre edilmelidir.
/// PR #7 makul bir varsayılan uygular; kalibrasyon fiyat motoru/parite ile gelecek.
/// </summary>
public static class VatMath
{
    /// <summary>Varsayılan KDV oranı (Türkiye standart %20; canlı referans sistem Kdv_Sistemi=20 kalibrasyonu).
    /// Fatura varsayılanı + fiyat KDV-modu net↔brüt çevrimi bunu kullanır (tek kaynak).</summary>
    public const decimal DefaultRate = 0.20m;

    public static (decimal Net, decimal Kdv) FromGross(decimal gross, decimal vatRate)
    {
        if (vatRate < 0) throw new ArgumentOutOfRangeException(nameof(vatRate));
        // Önce brütü kuruşa sabitle ki hem net hem KDV 2 ondalık olsun (e-Fatura kuruş kuralı).
        gross = Math.Round(gross, 2, MidpointRounding.AwayFromZero);
        var net = Math.Round(gross / (1 + vatRate), 2, MidpointRounding.AwayFromZero);
        var vat = gross - net; // gross 2dp, net 2dp → kdv 2dp; net+kdv = gross (denge korunur)
        return (net, vat);
    }

    /// <summary>Brütün kuruşa yuvarlanmış (fatura GenelToplam'ı için tutarlı) hali.</summary>
    public static decimal RoundGross(decimal gross) => Math.Round(gross, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Net tutardan KDV + brüt: KDV = round(net*oran, 2), brüt = net + KDV. (Gider/alış girişi:
    /// tedarikçi faturası net+KDV verir.) net 2dp varsayılır; kuruş tutarlı.
    /// </summary>
    public static (decimal Kdv, decimal Gross) FromNet(decimal net, decimal vatRate)
    {
        if (vatRate < 0) throw new ArgumentOutOfRangeException(nameof(vatRate));
        net = Math.Round(net, 2, MidpointRounding.AwayFromZero);
        var vat = Math.Round(net * vatRate, 2, MidpointRounding.AwayFromZero);
        return (vat, net + vat);
    }
}
