namespace RentACar.Application.Reporting;

/// <summary>
/// Kalıntı değer projeksiyonu (FAZ 2.4) — SALT-HESAP, deftere/bakiyeye yazmaz (MaliyetHesap deseni).
/// Azalan bakiye: yıllık oran GÖZLENEN amortismandan türetilir — (İkinciEl/Alım)^(1/yaş-yıl);
/// yaş &lt; 1 yıl veya alım verisi yoksa şeffaf varsayılan 0.85. Oran 1.00 ile SINIRLANIR (değer
/// ARTIŞI projekte edilmez — enflasyonist piyasada bile muhafazakâr kalınır; gösterilen oran ==
/// kullanılan oran olsun diye oran 2 haneye yuvarlanıp projeksiyon o yuvarlak oranla yapılır).
/// Taban güncel İkinciElDeğer'dir; o yoksa projeksiyon YOK (uydurma taban yok) → null.
/// </summary>
public static class KalintiProjeksiyon
{
    public const decimal VarsayilanYillikOran = 0.85m;

    public static KalintiProjeksiyonDto? Hesapla(
        decimal? alimBedeli, decimal? ikinciElDeger, DateTimeOffset? alimTarihi, DateTimeOffset simdi)
    {
        if (ikinciElDeger is not > 0m) return null;

        decimal? yasYil = alimTarihi is { } at && simdi > at
            ? (decimal)((simdi - at).TotalDays / 365.25) : null;

        decimal oran; bool gozlenen;
        if (alimBedeli is > 0m && yasYil is >= 1m)
        {
            var o = Math.Pow((double)(ikinciElDeger.Value / alimBedeli.Value), 1d / (double)yasYil.Value);
            oran = Math.Min(1.00m, decimal.Round((decimal)o, 2, MidpointRounding.AwayFromZero));
            gozlenen = true;
        }
        else { oran = VarsayilanYillikOran; gozlenen = false; }

        var d12 = decimal.Round(ikinciElDeger.Value * oran, 2, MidpointRounding.AwayFromZero);
        var d24 = decimal.Round(d12 * oran, 2, MidpointRounding.AwayFromZero);
        return new KalintiProjeksiyonDto(oran, gozlenen, d12, d24);
    }
}
