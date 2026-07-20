using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RentACar.Application.BelgeSablon;
using RentACar.Domain.Entities;

namespace RentACar.Web.Reports;

/// <summary>
/// PDF export (roadmap F4): kira sözleşmesi + fatura PDF'i (QuestPDF). Community lisansı kod-içi set edilir
/// (kimliksiz; runtime key/credential GEREKMEZ). Sunum katmanı; veri servis/repo'dan.
/// </summary>
public sealed class PdfExportService
{
    static PdfExportService() => QuestPDF.Settings.License = LicenseType.Community;

    // Ekspertiz araç şeması — orijinal sözleşmeden gömülü görsel (çıkış+dönüş, gösterge+ekipman+araç tek karede).
    private static readonly byte[] EkspertizSema = LoadEmbedded("ekspertiz-sema.png");
    private static byte[] LoadEmbedded(string suffix)
    {
        var asm = typeof(PdfExportService).Assembly;
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        using var s = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    // Çerçeve/renkler — canlı TürevRent sözleşme paritesi (yoğun ızgara).
    private const string Line = "#4b5563";
    private const string LabelBg = "#f1f5f9";

    /// <summary>Kira sözleşmesi PDF'i — canlı YÜCE RENT sözleşmesinin BİREBİR düzeni (çerçeveli 4-sütun ızgara
    /// + çift ekspertiz araç şeması + iki dilli hukuki metin + 3 imza). HTML-print ile aynı SozlesmeView'den
    /// (içerik tek kaynak). Değişkenler kiraya bağlı — her sözleşme aynı şablon, sadece veri değişir.</summary>
    public byte[] Contract(RentACar.Application.Bookings.SozlesmeView s) =>
        Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(24);
                p.DefaultTextStyle(t => t.FontSize(8).FontColor("#111827"));
                var pb = string.Equals(RentACar.Application.Kur.KurService.NormalizeKod(s.Doviz), "TRY",
                    StringComparison.OrdinalIgnoreCase) ? "TL" : (s.Doviz ?? "TL");
                string DT(DateTimeOffset? d) => d is { } x ? x.LocalDateTime.ToString("dd.MM.yyyy") : "";
                string Sa(DateTimeOffset? d) => d is { } x ? x.LocalDateTime.ToString("HH:mm") : "";
                static string Dots(int n) => new('.', n);

                // Marka-özel şablon metinleri: token'lar ({BelgeNo}/{Tarih}/{Firma*}) burada konur.
                // Şablon bölümü null → koddaki varsayılan (BelgeSablonVarsayilan) → şablonsuz çıktı aynı.
                var tk = new Dictionary<string, string?>
                {
                    ["FirmaUnvan"] = s.FirmaUnvan, ["FirmaMarka"] = s.FirmaMarka, ["FirmaVergiNo"] = s.FirmaVergiNo,
                    ["BelgeNo"] = s.SozlesmeNo, ["Tarih"] = s.BasTar.LocalDateTime.ToString("dd.MM.yyyy")
                };
                string Metin(string? sablon, string varsayilan) => SablonToken.Uygula(sablon ?? varsayilan, tk) ?? varsayilan;

                // ---- ÜST BAŞLIK ----
                p.Header().Row(r =>
                {
                    r.RelativeItem().Column(c =>
                    {
                        if (s.FirmaLogo is { Length: > 0 } logo)
                            c.Item().PaddingBottom(3).Height(38).AlignLeft().Image(logo).FitHeight(); // PR-C firma logosu
                        c.Item().Text(Metin(s.SablonBaslik, BelgeSablonVarsayilan.SozlesmeBaslik)).FontSize(11).Bold();
                        if (s.FirmaTel is not null) c.Item().Text($"OFİS TEL : {s.FirmaTel}").FontSize(8).SemiBold();
                        if (s.FirmaMobilTel is not null) c.Item().Text($"MOBİL TEL : {s.FirmaMobilTel}").FontSize(8).SemiBold();
                        if (s.FirmaAdres is not null) c.Item().Text(s.FirmaAdres).FontSize(8).SemiBold();
                        if (s.FirmaUnvan is not null) c.Item().Text(s.FirmaUnvan).FontSize(8).SemiBold();
                    });
                    r.ConstantItem(180).Column(c =>
                    {
                        // Sağ üst: ticari MARKA (yoksa hukuki ünvan).
                        c.Item().AlignRight().Text((s.FirmaMarka ?? s.FirmaUnvan ?? "RENT A CAR").ToUpperInvariant()).FontSize(11).Bold();
                        if (s.FirmaVergiNo is not null)
                            c.Item().AlignRight().Text($"{s.FirmaVergiDairesi} VD. {s.FirmaVergiNo}").FontSize(8).SemiBold();
                        c.Item().PaddingTop(4).AlignRight().Text(s.SozlesmeNo).FontSize(14).Bold();
                    });
                });

                p.Content().PaddingTop(6).Column(col =>
                {
                    // ========== ANA IZGARA — SOL müşteri / SAĞ araç BAĞIMSIZ tablolar ==========
                    // (referans: uzun alanlar [Adres/Fatura Adresi] sol sütunda boy alır, sağ sütun uzamaz).
                    col.Item().Row(r =>
                    {
                        r.RelativeItem(48).Table(t =>
                        {
                            t.ColumnsDefinition(c => { c.RelativeColumn(1.15f); c.RelativeColumn(1.85f); });
                            void L(string lbl, string? val, float minH = 0)
                            {
                                t.Cell().Element(LabelCell).Text(lbl);
                                t.Cell().Element(minH > 0 ? c => ValCell(c).MinHeight(minH) : ValCell).Text(val ?? "");
                            }
                            L("Adı Soyadı", s.MusteriAd);
                            L("T.C. / Pasaport No", s.TcKimlik);
                            L("Adres", s.MusteriAdres, 32);          // uzun → boy
                            L("Telefon", s.MusteriTel);
                            L("E-Mail", s.MusteriEmail);
                            L("Fatura Adresi", null, 24);            // uzun → boy
                            L("V. Dairesi / No", null);
                            L("1. Kullanıcı Tc", s.TcKimlik);
                            L("Ad Soyad", s.MusteriAd);
                            L("Ehliyet No / İl", $"{s.EhliyetNo} {s.EhliyetYeri}".Trim());
                            L("Verildiği Tarih", DT(s.EhliyetTarihi));
                            L("Doğum Tarihi", DT(s.DogumTarihi));
                            L("2. Sürücü Ad Soyad", s.IkinciSurucuAd);
                            L("2. Ehliyet / Doğum", s.IkinciSurucuAd is null ? null
                                : $"{s.IkinciEhliyetNo} {s.IkinciEhliyetYeri} {DT(s.IkinciDogumTarihi)}".Trim());
                        });
                        r.ConstantItem(6);
                        // SAĞ: 4-sütun yoğun ızgara — araç bilgisi | mali (referans paritesi, tüm alanlar).
                        r.RelativeItem(52).Table(t =>
                        {
                            t.ColumnsDefinition(c => { c.RelativeColumn(1f); c.RelativeColumn(1.3f); c.RelativeColumn(1f); c.RelativeColumn(1f); });
                            void Lb(string x) => t.Cell().Element(LabelCell).Text(x);
                            void Vl(string? x) => t.Cell().Element(ValCell).Text(x ?? "");
                            void Row(string al, string? av, string ml, string? mv) { Lb(al); Vl(av); Lb(ml); Vl(mv); }
                            string M(decimal? d) => d is { } x ? $"{x:N2} {pb}" : "";

                            // Referans (YÜCE RENT) satır SIRASI birebir: araç sol / mali sağ. Marka-Model ve
                            // Çıkış-Dönüş-Yapılan Km AYRI satır; mali kolon hedef sırasıyla (Gün Sayısı→Hazırlayan).
                            // Değeri olmayan alanlar (Fazla Saat/Kasko/Ödeme Şekli/Hasarlı/Rez/Dosya) hedefte de boş.
                            Row("Kiralandığı Yer", $"{s.CikisOfisi ?? ""} {DT(s.BasTar)} {Sa(s.BasTar)}".Trim(), "Gün Sayısı", s.Gun.ToString());
                            Row("Döneceği Tarih", $"{DT(s.BitTar)} {Sa(s.BitTar)}".Trim(), "Fazla Saat", null);
                            Row("Döndüğü Yer", $"{s.DonusOfisi ?? ""} {DT(s.GercekDonusTar)} {Sa(s.GercekDonusTar)}".Trim(), "Drop", M(s.DropUcreti));
                            Row("Plaka", s.Plaka, "Kasko", null);
                            Row("Marka", s.Marka, "Depozit", M(s.Depozito));
                            Row("Model", $"{s.Tip}{(s.ModelYili is null ? "" : $" ({s.ModelYili})")}".Trim(), "Ödeme Şekli", null);
                            Row("Çıkış Km", s.CikisKm?.ToString(), "Tahsilat", M(s.Tahsilat));
                            Row("Dönüş Km", s.DonusKm?.ToString(), "Kalan", M(s.Bakiye));
                            Row("Yapılan Km", s.KullanilanKm?.ToString(), "G. TOPLAM", M(s.GenelToplam));
                            Row("Araç Grup", s.Grup, "Hazırlayan", s.TeslimAlanAd);
                            Row("Hasarlı Araç", null, "", null);
                            Row("Rez Kaynağı", null, "", null);
                            Row("Dosya No", null, "", null);
                        });
                    });

                    // ========== AÇIKLAMA (tam genişlik) ==========
                    // Operatörün serbest metni ÖNCE (hedef paritesi: MUAFİYET/AŞIM KM/TOTAL KM gibi kritik
                    // notlar bu banttadır) — eskiden hiç basılmıyordu; sonra hesaplanmış ek bilgiler.
                    var acik = new List<string>();
                    if (!string.IsNullOrWhiteSpace(s.Aciklama)) acik.Add(s.Aciklama.Trim());
                    if (s.KmHediye is int kh && kh > 0) acik.Add($"KM Hediye: {kh}");
                    if (s.BitisSebebi is not null) acik.Add($"Bitiş Sebebi: {s.BitisSebebi}");
                    if (s.HediyeGun is int hg && hg > 0) acik.Add($"Hediye {hg} gün (faturalanan {s.FaturalananGun})");
                    if (s.HaftaSonuFark is decimal hs && hs > 0) acik.Add($"Hafta sonu +{hs:N2}");
                    if (s.IskontoTutar is decimal isk && isk > 0) acik.Add($"İskonto −{isk:N2}");
                    if (s.EkHizmetler.Count > 0) acik.Add($"Ek Hizmet: {string.Join(", ", s.EkHizmetler.Select(e => $"{e.Ad} {e.Toplam:N2}"))}");
                    col.Item().BorderHorizontal(0.75f).BorderColor(Line).Background(LabelBg).MinHeight(26).PaddingHorizontal(4).PaddingVertical(3)
                        .Text($"Açıklama : {(acik.Count == 0 ? "" : string.Join("  ·  ", acik))}").FontSize(8);
                    col.Item().Border(0.75f).BorderColor(Line).PaddingHorizontal(4).PaddingVertical(2)
                        .Text($"Günlük {s.GunlukUcret:N2}   ·   Kira {s.Tutar:N2}   ·   KM Limit {(s.KmLimit == 0 ? "sınırsız" : s.KmLimit.ToString())} / Aşım {s.FazlaKmUcret:N2}   ·   Fazla KM {s.FazlaKmBedeli:N2}   ·   Yakıt {s.YakitBedeli:N2}   ·   Uzatma {s.UzatmaBedeli:N2}   ·   Ek Hizmet {s.EkHizmetToplam:N2}   ({pb})").FontSize(8);

                    // ========== ÇİFT EKSPERTİZ (orijinal araç şeması görseli — çıkış+dönüş tek karede) ==========
                    col.Item().PaddingTop(2).Border(0.75f).BorderColor(Line).Image(EkspertizSema).FitWidth();

                    // ========== HUKUKİ METİN (iki dilli) ==========
                    col.Item().PaddingTop(2).Row(r =>
                    {
                        r.RelativeItem().Border(0.75f).BorderColor(Line).Padding(4)
                            .Text(Metin(s.SablonHukukiSol, BelgeSablonVarsayilan.SozlesmeHukukiSol)).FontSize(7);
                        r.RelativeItem().BorderVertical(0.75f).BorderRight(0.75f).BorderColor(Line).Padding(4)
                            .Text(Metin(s.SablonHukukiSag, BelgeSablonVarsayilan.SozlesmeHukukiSag)).FontSize(7);
                    });

                    // ========== EK KOŞULLAR (FAZ 4.4 — kira-özel şartlar; varsa basılır) ==========
                    if (!string.IsNullOrWhiteSpace(s.EkKosullar))
                        col.Item().PaddingTop(2).Border(0.75f).BorderColor(Line).Padding(4).Column(ek =>
                        {
                            ek.Item().Text("EK KOŞULLAR / ADDITIONAL TERMS").FontSize(7.5f).Bold();
                            ek.Item().Text(s.EkKosullar!).FontSize(7);
                        });

                    // ========== KREDİ KARTI + KART SAHİBİ + İMZA (referans YÜCE RENT birebir) ==========
                    // Elle doldurulan BOŞ alanlar — PCI: kart verisi sistemde TUTULMAZ, sözleşmede fizikî alınır.
                    col.Item().PaddingTop(2).Border(0.75f).BorderColor(Line).Row(r =>
                    {
                        r.RelativeItem(1.15f).Padding(5).Column(c =>
                        {
                            c.Item().PaddingBottom(5).Text($"Kart No : {Dots(24)}").FontSize(7.5f);
                            c.Item().PaddingBottom(5).Text($"CVV No : {Dots(24)}").FontSize(7.5f);
                            c.Item().PaddingBottom(5).Text($"Banka / Cinsi : {Dots(20)}").FontSize(7.5f);
                            c.Item().Text($"Son Kullanma Tarihi : {Dots(15)}").FontSize(7.5f);
                        });
                        r.RelativeItem(1f).BorderLeft(0.75f).BorderColor(Line).Padding(5).Column(c =>
                        {
                            c.Item().PaddingBottom(3).Text("Kart Sahibinin").FontSize(7.5f).Bold();
                            c.Item().PaddingBottom(5).Text($"Adı Soyadı : {Dots(16)}").FontSize(7.5f);
                            c.Item().PaddingBottom(5).Text($"Telefonu : {Dots(16)}").FontSize(7.5f);
                            c.Item().Text($"İmza : {Dots(18)}").FontSize(7.5f);
                        });
                        r.RelativeItem(1f).BorderLeft(0.75f).BorderColor(Line).Padding(5).Column(c =>
                        {
                            c.Item().Text("AD SOYAD - NAME SURNAME").FontSize(7.5f).Bold();
                            c.Item().PaddingTop(2).Text(s.MusteriAd).FontSize(8);
                            c.Item().Height(22);
                            c.Item().Text("İMZA - SİGNATURE").FontSize(7.5f).Bold();
                        });
                    });
                });

                // Marka-özel alt bilgi (şablonda tanımlıysa; yoksa footer basılmaz — mevcut düzen korunur).
                if (!string.IsNullOrWhiteSpace(s.SablonAltBilgi))
                    p.Footer().PaddingTop(4).AlignCenter().Text(Metin(s.SablonAltBilgi, "")).FontSize(8).FontColor(Line);
            });
        }).GeneratePdf();

    private static IContainer LabelCell(IContainer c) => c.Border(0.5f).BorderColor(Line).Background(LabelBg).PaddingHorizontal(3).PaddingVertical(2.5f);
    private static IContainer ValCell(IContainer c) => c.Border(0.5f).BorderColor(Line).PaddingHorizontal(3).PaddingVertical(2.5f);

    /// <summary>Generic tablo PDF'i — TÜM liste/rapor export'larının ortak PDF çıktısı (Excel/CSV ile AYNI
    /// veri; başlık + sütun başlıkları + satırlar). Landscape A4 (geniş tablolar için), tip-duyarlı hücre
    /// biçimleme (decimal→N2, tarih→dd.MM.yyyy, bool→Evet/Hayır). Uçlar ?format=pdf ile bunu çağırır.</summary>
    public byte[] Table(string baslik, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows) =>
        Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4.Landscape());
                p.Margin(20);
                p.DefaultTextStyle(t => t.FontSize(7).FontColor("#111827"));

                p.Header().PaddingBottom(6).Column(c =>
                {
                    c.Item().Text(baslik).FontSize(13).Bold();
                    c.Item().Text($"{rows.Count} kayıt · {DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(7).FontColor("#6b7280");
                });

                p.Content().Table(tbl =>
                {
                    tbl.ColumnsDefinition(cd => { foreach (var _ in headers) cd.RelativeColumn(); });
                    tbl.Header(h =>
                    {
                        foreach (var head in headers)
                            h.Cell().Background(LabelBg).Border(0.5f).BorderColor(Line).Padding(3).Text(head).Bold();
                    });
                    foreach (var row in rows)
                        foreach (var cell in row)
                            tbl.Cell().Border(0.5f).BorderColor(Line).Padding(3).Text(Fmt(cell));
                });

                p.Footer().AlignRight().Text(x => { x.Span("Sayfa "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        }).GeneratePdf();

    // Generic tablo hücresi biçimleyici (tip-duyarlı).
    private static string Fmt(object? c) => c switch
    {
        null => "",
        decimal d => d.ToString("N2"),
        DateTimeOffset dto => dto.LocalDateTime.ToString("dd.MM.yyyy"),
        DateTime dt => dt.ToString("dd.MM.yyyy"),
        bool b => b ? "Evet" : "Hayır",
        _ => c.ToString() ?? ""
    };

    // PR-C: markalı fatura (firma başlığı + logo TenantSettings'ten; hard-coded "Fatura"/"RentPro" kaldırıldı).
    // Marka-özel şablon (opsiyonel): başlık + alt bilgi override (null → koddaki varsayılan; çıktı aynı).
    public byte[] Invoice(Invoice inv, PdfMarka marka, string? cariAd, SablonMetin? sablon = null) =>
        Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(40);
                p.DefaultTextStyle(t => t.FontSize(9).FontColor("#111827"));
                var tk = MarkaTokenlari(marka, inv.No, $"{inv.Tarih:dd.MM.yyyy}");
                var baslik = SablonToken.Uygula(sablon?.Baslik, tk)
                    ?? (inv.IadeMi ? "İADE FATURASI" : BelgeSablonVarsayilan.FaturaBaslik);
                var altBilgi = SablonToken.Uygula(sablon?.AltBilgi, tk)
                    ?? $"{marka.Marka ?? marka.Unvan ?? ""} — {inv.No}";
                p.Header().Element(h => MarkaBaslik(h, marka, baslik, inv.No,
                    $"Tarih: {inv.Tarih:dd.MM.yyyy}" + (inv.VadeTarihi is { } v ? $"  ·  Vade: {v:dd.MM.yyyy}" : "")));
                p.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(5);
                    if (!string.IsNullOrWhiteSpace(cariAd)) col.Item().Text($"Sayın: {cariAd}").SemiBold();
                    col.Item().PaddingTop(6).Text("Kalemler").SemiBold();
                    foreach (var l in inv.Lines)
                        col.Item().Text($"  • {l.Aciklama}   ×{l.Miktar:N2}   (KDV %{l.KdvOrani * 100:N0})   = {l.SatirToplam:N2}");
                    col.Item().PaddingTop(8).AlignRight().Text($"Net: {inv.NetTutar:N2}     KDV: {inv.KdvTutar:N2}");
                    col.Item().AlignRight().Text($"Genel Toplam: {inv.GenelToplam:N2} {inv.Currency}").FontSize(12).Bold();
                });
                p.Footer().AlignCenter().Text(altBilgi).FontSize(8).FontColor(Line);
            });
        }).GeneratePdf();

    // PR-C: yeni belge türü — tahsilat/ödeme makbuzu (CashTransaction'dan; markalı).
    // Marka-özel şablon (opsiyonel): başlık + alt bilgi override (Tahsilat/Ödeme ayrımı, şablon başlık boşsa korunur).
    public byte[] TahsilatMakbuzu(CashTransaction tx, PdfMarka marka, string? cariAd, SablonMetin? sablon = null) =>
        Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A5.Landscape());
                p.Margin(30);
                p.DefaultTextStyle(t => t.FontSize(10).FontColor("#111827"));
                var makbuzTip = tx.Tip == RentACar.Domain.Enums.CashTransactionType.Tahsilat ? "TAHSİLAT MAKBUZU" : "ÖDEME MAKBUZU";
                var tk = MarkaTokenlari(marka, tx.No, $"{tx.Tarih:dd.MM.yyyy}");
                var baslik = SablonToken.Uygula(sablon?.Baslik, tk) ?? makbuzTip;
                var altBilgi = SablonToken.Uygula(sablon?.AltBilgi, tk) ?? $"{marka.Marka ?? marka.Unvan ?? ""} — {tx.No}";
                p.Header().Element(h => MarkaBaslik(h, marka, baslik, tx.No, $"Tarih: {tx.Tarih:dd.MM.yyyy}"));
                p.Content().PaddingVertical(16).Column(col =>
                {
                    col.Spacing(8);
                    var yon = tx.Tip == RentACar.Domain.Enums.CashTransactionType.Tahsilat ? "alınmıştır" : "ödenmiştir";
                    col.Item().Text($"Sayın {cariAd ?? "-"},").SemiBold();
                    col.Item().Text($"Aşağıdaki tutar {tx.KarsiHesap} hesabından {yon}.");
                    col.Item().PaddingTop(6).Border(0.75f).BorderColor(Line).Padding(8).Row(r =>
                    {
                        r.RelativeItem().Text("Tutar").SemiBold();
                        r.ConstantItem(180).AlignRight().Text($"{tx.Amount.Amount:N2} {tx.Amount.Currency}").FontSize(14).Bold();
                    });
                    if (!string.IsNullOrWhiteSpace(tx.Aciklama)) col.Item().Text($"Açıklama: {tx.Aciklama}").FontSize(9);
                    col.Item().PaddingTop(24).Row(r =>
                    {
                        r.RelativeItem().AlignCenter().Text("Teslim Eden").FontSize(9);
                        r.RelativeItem().AlignCenter().Text("Teslim Alan").FontSize(9);
                    });
                });
                p.Footer().AlignCenter().Text(altBilgi).FontSize(8).FontColor(Line);
            });
        }).GeneratePdf();

    /// <summary>Fatura/makbuz şablon token sözlüğü ({FirmaUnvan}/{FirmaMarka}/{FirmaVergiNo}/{BelgeNo}/{Tarih}).</summary>
    private static Dictionary<string, string?> MarkaTokenlari(PdfMarka m, string belgeNo, string tarih) => new()
    {
        ["FirmaUnvan"] = m.Unvan, ["FirmaMarka"] = m.Marka, ["FirmaVergiNo"] = m.VergiNo,
        ["BelgeNo"] = belgeNo, ["Tarih"] = tarih
    };

    /// <summary>Ortak markalı başlık (logo + firma solda; belge adı + no + tarih sağda). Invoice + Makbuz kullanır.</summary>
    private static void MarkaBaslik(IContainer h, PdfMarka m, string baslik, string no, string? sagAlt) =>
        h.Row(r =>
        {
            r.RelativeItem().Column(c =>
            {
                if (m.Logo is { Length: > 0 } logo) c.Item().PaddingBottom(3).Height(40).AlignLeft().Image(logo).FitHeight();
                c.Item().Text((m.Marka ?? m.Unvan ?? "").ToUpperInvariant()).FontSize(13).Bold();
                if (m.Unvan is not null && m.Marka is not null) c.Item().Text(m.Unvan).FontSize(8);
                if (m.Adres is not null) c.Item().Text(m.Adres).FontSize(8);
                if (m.Tel is not null) c.Item().Text($"Tel: {m.Tel}").FontSize(8);
                if (m.VergiNo is not null) c.Item().Text($"{m.VergiDairesi} VD. {m.VergiNo}").FontSize(8);
            });
            r.ConstantItem(180).Column(c =>
            {
                c.Item().AlignRight().Text(baslik).FontSize(16).Bold();
                c.Item().AlignRight().Text(no).FontSize(12).SemiBold();
                if (sagAlt is not null) c.Item().AlignRight().Text(sagAlt).FontSize(9);
            });
        });
}

/// <summary>PDF başlığı için firma marka bilgisi (TenantSettings'ten; PR-C). Logo opsiyonel byte[] (PNG/JPG).</summary>
public sealed record PdfMarka(byte[]? Logo, string? Unvan, string? Marka, string? Adres, string? Tel, string? VergiDairesi, string? VergiNo);
