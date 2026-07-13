namespace RentACar.Application.Reporting;

/// <summary>
/// Tut/Sat (defleet) sinyali — FAZ 2.2. Üç kural, tümü ELDEKİ veriden (kalibrasyonsuz şeffaf eşikler):
/// (a) son-12-ay araç gideri ÷ İkinciElDeğer &gt; DegerOrani (bakım değmez — sat);
/// (b) aynı oran, sınıf (Grup) ortalamasının SinifKati katından büyük (sınıfından pahalı);
/// (c) km-başı maliyet son-12-ay &gt; önceki-12-ay (maliyet eğrisi yukarı kırıldı).
/// Veri yoksa kural TETİKLENMEZ (İkinciEl yok → a/b yok; km penceresi boş → c yok) — yanlış-pozitif yok.
/// Tek-araç sınıfta (b) tetiklenmez (oran ortalamanın katı olamaz). Karne kartı + filo kolonu ortak.
/// </summary>
public static class TutSatHesap
{
    public static TutSatSinyalDto Hesapla(
        FiloTutSatRow ham, decimal? ikinciElDeger, decimal? grupOrtDegerOrani, TutSatEsikleri esik)
    {
        var gerekceler = new List<string>();

        decimal? oran = ikinciElDeger is > 0m ? ham.Gider12 / ikinciElDeger.Value : null;
        if (oran is { } o1 && o1 > esik.DegerOrani)
            gerekceler.Add($"Son 12 ay gideri ({ham.Gider12:N0}) ikinci el değerin %{esik.DegerOrani * 100:N0}'ini aştı (oran {o1:P0}).");

        if (oran is { } o2 && grupOrtDegerOrani is > 0m && o2 > grupOrtDegerOrani.Value * esik.SinifKati)
            gerekceler.Add($"Gider/değer oranı ({o2:P0}) sınıf ortalamasının ({grupOrtDegerOrani.Value:P0}) {esik.SinifKati:0.#} katını aştı.");

        decimal? kmM12 = ham.Km12 > 0 ? ham.Gider12 / ham.Km12 : null;
        decimal? kmMOnceki = ham.KmOnceki12 > 0 ? ham.GiderOnceki12 / ham.KmOnceki12 : null;
        if (kmM12 is { } m1 && kmMOnceki is { } m0 && m1 > m0)
            gerekceler.Add($"Km-başı maliyet yükseldi: önceki 12 ay {m0:N2} → son 12 ay {m1:N2}.");

        return new TutSatSinyalDto(gerekceler.Count, gerekceler);
    }
}
