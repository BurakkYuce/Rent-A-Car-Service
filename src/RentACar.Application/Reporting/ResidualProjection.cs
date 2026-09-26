namespace RentACar.Application.Reporting;

/// <summary>
/// Kalıntı değer projeksiyonu (FAZ 2.4) — SALT-HESAP, deftere/bakiyeye yazmaz (MaliyetHesap deseni).
/// Azalan bakiye: yıllık oran GÖZLENEN amortismandan türetilir — (İkinciEl/Alım)^(1/yaş-yıl);
/// yaş &lt; 1 yıl veya alım verisi yoksa şeffaf varsayılan 0.85. Oran 1.00 ile SINIRLANIR (değer
/// ARTIŞI projekte edilmez — enflasyonist piyasada bile muhafazakâr kalınır; gösterilen oran ==
/// kullanılan oran olsun diye oran 2 haneye yuvarlanıp projeksiyon o yuvarlak oranla yapılır).
/// Taban güncel İkinciElDeğer'dir; o yoksa projeksiyon YOK (uydurma taban yok) → null.
/// </summary>
public static class ResidualProjection
{
    public const decimal DefaultAnnualRate = 0.85m;

    public static KalintiProjeksiyonDto? Calculate(
        decimal? purchasePrice, decimal? secondHandValue, DateTimeOffset? purchaseDate, DateTimeOffset now)
    {
        if (secondHandValue is not > 0m) return null;

        decimal? ageYears = purchaseDate is { } at && now > at
            ? (decimal)((now - at).TotalDays / 365.25) : null;

        decimal rate; bool observed;
        if (purchasePrice is > 0m && ageYears is >= 1m)
        {
            var o = Math.Pow((double)(secondHandValue.Value / purchasePrice.Value), 1d / (double)ageYears.Value);
            rate = Math.Min(1.00m, decimal.Round((decimal)o, 2, MidpointRounding.AwayFromZero));
            observed = true;
        }
        else { rate = DefaultAnnualRate; observed = false; }

        var d12 = decimal.Round(secondHandValue.Value * rate, 2, MidpointRounding.AwayFromZero);
        var d24 = decimal.Round(d12 * rate, 2, MidpointRounding.AwayFromZero);
        return new KalintiProjeksiyonDto(rate, observed, d12, d24);
    }
}
