using RentACar.Application.Reporting;

namespace RentACar.Web.Reports;

/// <summary>
/// Araç Karnesi + Filo Analiz export projeksiyonları — saf, test-edilebilir (ListExportCatalog deseni).
/// Karne: tek sayfalık Metrik/Değer özeti (kimlik + dönem P&amp;L + ömür-boyu KPI + model + yıllık + kırılım).
/// Filo: satır tablosu + "(Atanmamış)" + TOPLAM (defter mutabakatı satırı). Para mantığı YOK — yalnız eşleme.
/// </summary>
public static class ScorecardExportCatalog
{
    public static ExportTable? VehicleScorecard(AracKarneDto? d)
    {
        if (d is null) return null;
        var h = d.Header;
        var k = d.Kpi;
        var rows = new List<object?[]>();
        rows.Add(["Plaka", h.Plaka]);
        rows.Add(["Marka / Tip", $"{h.Marka} {h.Tip}".Trim()]);
        rows.Add(["Grup / Segment / Şube", $"{h.Grup} / {h.Segment} / {h.Sube}"]);
        rows.Add(["Durum", h.Durum.ToString()]);
        rows.Add(["KM", h.Km]);
        rows.Add(["Alım Bedeli", h.AlimBedeli]);
        rows.Add(["Alım Tarihi", ExportDate.Day(h.AlimTarihi)]);
        rows.Add(["İkinci El Değeri", h.IkinciElDeger]);
        rows.Add(["— P&L (dönem) —", null]);
        rows.Add(["Gelir", d.ToplamGelir]);
        rows.Add(["Gider", d.ToplamGider]);
        rows.Add(["Net Kâr (defter)", d.ToplamNetKar]);
        rows.Add(["— KPI (ömür boyu) —", null]);
        rows.Add(["Doluluk %", k.DolulukYuzde]);
        rows.Add(["RevPACD", k.RevPacd]);
        rows.Add(["ADR", k.Adr]);
        rows.Add(["Km Başına Maliyet", k.KmBasinaMaliyet]);
        rows.Add(["Net Marj %", k.NetMarjYuzde]);
        rows.Add(["ROI %", k.RoiYuzde]);
        rows.Add(["Geri Ödeme (ay)", k.GeriOdemeAy]);
        rows.Add(["TCO", k.Tco]);
        rows.Add(["Gerçekleşen Amortisman", k.GerceklesenAmortisman]);
        rows.Add(["Aylık Amortisman", k.AylikAmortisman]);
        rows.Add(["Ekonomik Kâr", k.EkonomikKar]);
        rows.Add(["Sahiplik / Kiralanan / Servis / Boş (gün)",
            $"{k.SahiplikGun} / {k.KiralananGun} / {k.ServisGun} / {k.BosGun}"]);
        rows.Add(["Kira Sayısı", k.KiraSayisi]);
        rows.Add(["Katedilen KM", k.ToplamKatedilenKm]);
        if (d.MaliyetModel is { } m)
        {
            rows.Add(["— Amortisman/Başabaş Modeli —", null]);
            rows.Add(["Kalıntı Değer", m.ResidualDeger]);
            rows.Add(["Net Amortisman (model)", m.NetAmortisman]);
            rows.Add(["Başabaş (aylık)", m.BasaBasAylik]);
        }
        if (d.YillikPnl.Count > 0)
        {
            rows.Add(["— Yıllık P&L —", null]);
            foreach (var y in d.YillikPnl)
                rows.Add([$"{y.Yil} Gelir / Gider / Net", $"{y.Gelir} / {y.Gider} / {y.NetKar}"]);
        }
        foreach (var g in d.GelirKaynak) rows.Add([$"Gelir: {g.Kategori}", g.Tutar]);
        foreach (var g in d.GiderKategori) rows.Add([$"Gider: {g.Kategori}", g.Tutar]);

        return new ExportTable($"Araç Karnesi {h.Plaka}", ["Metrik", "Değer"], rows);
    }

    public static ExportTable FleetAnalysis(FiloAnalizDto d)
    {
        var rows = d.Satirlar.Select(r => new object?[]
        {
            r.Plaka, r.Grup, r.Segment, r.Sube, r.Gelir, r.Gider, r.NetKar,
            r.DolulukYuzde, r.RoiYuzde, r.KmBasinaMaliyet, r.YasAy
        }).ToList();
        if (d.AtanmamisGelir != 0m || d.AtanmamisGider != 0m)
            rows.Add(["(Atanmamış)", null, null, null, d.AtanmamisGelir, d.AtanmamisGider,
                d.AtanmamisGelir - d.AtanmamisGider, null, null, null, null]);
        rows.Add(["TOPLAM", null, null, null, d.ToplamGelir, d.ToplamGider, d.ToplamNetKar,
            null, null, null, null]);
        return new ExportTable("Filo Analiz",
            ["Plaka", "Grup", "Segment", "Şube", "Gelir", "Gider", "Net Kâr", "Doluluk %", "ROI %", "Km Maliyet", "Yaş (ay)"],
            rows);
    }
}
