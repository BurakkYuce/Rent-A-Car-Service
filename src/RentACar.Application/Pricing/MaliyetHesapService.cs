using RentACar.Application.Common;

namespace RentACar.Application.Pricing;

/// <summary>
/// Filo (uzun-dönem) maliyet hesaplayıcı (roadmap L2). SALT-HESAP — defter/bakiye/kayıt YOK.
/// QuoteCalculator (/fiyat-hesapla, günlük kira teklifi) ile FARKLI; bu, araç satın alıp uzun-dönem
/// kiralamanın maliyet/başabaş/kâr/teklif modeli. Şeffaf "makul varsayım" formülleri (canlı parite değil).
/// </summary>
public static class MaliyetHesapService
{
    public static MaliyetHesapSonuc Hesapla(MaliyetHesapInput x)
    {
        if (x.AlisBedeli <= 0m) throw new ValidationException("Alış bedeli pozitif olmalıdır.");
        if (x.SureAy is < 1 or > 120) throw new ValidationException("Süre (ay) 1 ile 120 arasında olmalıdır.");
        if (x.ResidualYuzde is < 0m or > 1m) throw new ValidationException("Kalıntı değer oranı 0-1 arası olmalıdır.");
        if (x.FaizOran < 0m || x.KkdfOran < 0m || x.BsmvOran < 0m || x.DamgaOran < 0m)
            throw new ValidationException("Oranlar negatif olamaz.");
        if (x.AylikGider < 0m) throw new ValidationException("Aylık gider negatif olamaz.");
        if (x.KarMarji < 0m) throw new ValidationException("Kâr marjı negatif olamaz.");
        if (x.KdvOran is < 0m or > 1m) throw new ValidationException("KDV oranı 0-1 arası olmalıdır.");

        var residualDeger = R(x.AlisBedeli * x.ResidualYuzde);
        var netAmortisman = x.AlisBedeli - residualDeger;                  // dönem boyu değer kaybı
        var finansmanFaiz = R(x.AlisBedeli * x.FaizOran * x.SureAy / 12m); // basit faiz, tam bedel
        var finansmanVergi = R(finansmanFaiz * (x.KkdfOran + x.BsmvOran)); // faiz üzerinden KKDF+BSMV
        var damga = R(x.AlisBedeli * x.DamgaOran);
        var toplamGider = R(x.AylikGider * x.SureAy);

        var toplamMaliyet = netAmortisman + finansmanFaiz + finansmanVergi + damga + toplamGider;
        var basaBasAylik = R(toplamMaliyet / x.SureAy);
        var kar = R(toplamMaliyet * x.KarMarji);
        var teklifNet = toplamMaliyet + kar;
        var teklifAylikNet = R(teklifNet / x.SureAy);
        var teklifKdvli = R(teklifNet * (1m + x.KdvOran));

        return new MaliyetHesapSonuc(
            residualDeger, netAmortisman, finansmanFaiz, finansmanVergi, damga, toplamGider,
            toplamMaliyet, basaBasAylik, kar, teklifNet, teklifAylikNet, teklifKdvli);
    }

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
