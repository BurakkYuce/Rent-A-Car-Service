namespace RentACar.Application.Reporting;

/// <summary>
/// Tut/Sat (defleet) sinyali — FAZ 2.2. Üç kural, tümü ELDEKİ veriden (kalibrasyonsuz şeffaf eşikler):
/// (a) son-12-ay araç gideri ÷ İkinciElDeğer &gt; DegerOrani (bakım değmez — sat);
/// (b) aynı oran, sınıf (Grup) ortalamasının SinifKati katından büyük (sınıfından pahalı);
/// (c) km-başı maliyet son-12-ay &gt; önceki-12-ay (maliyet eğrisi yukarı kırıldı).
/// Veri yoksa kural TETİKLENMEZ (İkinciEl yok → a/b yok; km penceresi boş → c yok) — yanlış-pozitif yok.
/// Tek-araç sınıfta (b) tetiklenmez (oran ortalamanın katı olamaz). Karne kartı + filo kolonu ortak.
/// </summary>
public static class HoldSellCalculation
{
    public static TutSatSinyalDto Calculate(
        FiloTutSatRow raw, decimal? secondHandValue, decimal? groupAvgValueRatio, TutSatEsikleri threshold)
    {
        var reasons = new List<string>();

        decimal? rate = secondHandValue is > 0m ? raw.Gider12 / secondHandValue.Value : null;
        if (rate is { } o1 && o1 > threshold.DegerOrani)
            reasons.Add($"Son 12 ay gideri ({raw.Gider12:N0}) ikinci el değerin %{threshold.DegerOrani * 100:N0}'ini aştı (oran {o1:P0}).");

        if (rate is { } o2 && groupAvgValueRatio is > 0m && o2 > groupAvgValueRatio.Value * threshold.SinifKati)
            reasons.Add($"Gider/değer oranı ({o2:P0}) sınıf ortalamasının ({groupAvgValueRatio.Value:P0}) {threshold.SinifKati:0.#} katını aştı.");

        decimal? kmM12 = raw.Km12 > 0 ? raw.Gider12 / raw.Km12 : null;
        decimal? kmMPrevious = raw.KmOnceki12 > 0 ? raw.GiderOnceki12 / raw.KmOnceki12 : null;
        if (kmM12 is { } m1 && kmMPrevious is { } m0 && m1 > m0)
            reasons.Add($"Km-başı maliyet yükseldi: önceki 12 ay {m0:N2} → son 12 ay {m1:N2}.");

        return new TutSatSinyalDto(reasons.Count, reasons);
    }
}
