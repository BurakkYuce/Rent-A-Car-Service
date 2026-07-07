using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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

    // Çerçeve/renkler — canlı referans sistem sözleşme paritesi (yoğun ızgara).
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

                // ---- ÜST BAŞLIK ----
                p.Header().Row(r =>
                {
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text("ARAÇ TESLİM BELGESİ / RENTAL AGREEMENT").FontSize(11).Bold();
                        if (s.FirmaTel is not null) c.Item().Text($"OFİS TEL : {s.FirmaTel}").FontSize(8).SemiBold();
                        if (s.FirmaAdres is not null) c.Item().Text(s.FirmaAdres).FontSize(8).SemiBold();
                        if (s.FirmaUnvan is not null) c.Item().Text(s.FirmaUnvan).FontSize(8).SemiBold();
                    });
                    r.ConstantItem(180).Column(c =>
                    {
                        c.Item().AlignRight().Text((s.FirmaUnvan ?? "RENT A CAR").ToUpperInvariant()).FontSize(11).Bold();
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

                            Row("Kiralandığı Yer", $"{s.CikisOfisi ?? ""} {DT(s.BasTar)} {Sa(s.BasTar)}".Trim(), "Gün Sayısı", s.Gun.ToString());
                            Row("Döneceği Tarih", $"{DT(s.BitTar)} {Sa(s.BitTar)}".Trim(), "Fazla Saat", null);
                            Row("Döndüğü Yer", s.DonusOfisi, "Drop", M(s.DropUcreti));
                            Row("Plaka", s.Plaka, "Kasko", null);
                            Row("Marka / Model", $"{s.Marka} {s.Tip}{(s.ModelYili is null ? "" : $" ({s.ModelYili})")}", "Depozit", M(s.Depozito));
                            Row("Araç Grup", s.Grup, "Ödeme Şekli", null);
                            Row("Çıkış / Dönüş Km", $"{s.CikisKm?.ToString() ?? ""} / {s.DonusKm?.ToString() ?? ""}", "KM Limit / Aşım", $"{(s.KmLimit == 0 ? "sınırsız" : s.KmLimit.ToString())} / {s.FazlaKmUcret:N2}");
                            Row("Yapılan Km", s.KullanilanKm?.ToString(), "Tahsilat", M(s.Tahsilat));
                            Row("Hasarlı Araç", null, "Kalan", M(s.Bakiye));
                            Row("Rez Kaynağı", null, "Hazırlayan", s.TeslimAlanAd);
                            Row("Dosya No", null, "G. TOPLAM", M(s.GenelToplam));
                        });
                    });

                    // ========== AÇIKLAMA (tam genişlik) ==========
                    var acik = new List<string>();
                    if (s.KmHediye is int kh && kh > 0) acik.Add($"KM Hediye: {kh}");
                    if (s.BitisSebebi is not null) acik.Add($"Bitiş Sebebi: {s.BitisSebebi}");
                    if (s.HediyeGun is int hg && hg > 0) acik.Add($"Hediye {hg} gün (faturalanan {s.FaturalananGun})");
                    if (s.HaftaSonuFark is decimal hs && hs > 0) acik.Add($"Hafta sonu +{hs:N2}");
                    if (s.IskontoTutar is decimal isk && isk > 0) acik.Add($"İskonto −{isk:N2}");
                    if (s.EkHizmetler.Count > 0) acik.Add($"Ek Hizmet: {string.Join(", ", s.EkHizmetler.Select(e => $"{e.Ad} {e.Toplam:N2}"))}");
                    col.Item().BorderHorizontal(0.75f).BorderColor(Line).Background(LabelBg).MinHeight(26).PaddingHorizontal(4).PaddingVertical(3)
                        .Text($"Açıklama : {(acik.Count == 0 ? "" : string.Join("  ·  ", acik))}").FontSize(8);
                    col.Item().Border(0.75f).BorderColor(Line).PaddingHorizontal(4).PaddingVertical(2)
                        .Text($"Günlük {s.GunlukUcret:N2}   ·   Kira {s.Tutar:N2}   ·   Fazla KM {s.FazlaKmBedeli:N2}   ·   Yakıt {s.YakitBedeli:N2}   ·   Uzatma {s.UzatmaBedeli:N2}   ·   Ek Hizmet {s.EkHizmetToplam:N2}   ({pb})").FontSize(8);

                    // ========== ÇİFT EKSPERTİZ (orijinal araç şeması görseli — çıkış+dönüş tek karede) ==========
                    col.Item().PaddingTop(2).Border(0.75f).BorderColor(Line).Image(EkspertizSema).FitWidth();

                    // ========== HUKUKİ METİN (iki dilli) ==========
                    col.Item().PaddingTop(2).Row(r =>
                    {
                        r.RelativeItem().Border(0.75f).BorderColor(Line).Padding(4).Text(
                            "By signing, the tenant has inspected the vehicle's damages and is responsible for the new damages.\n" +
                            "Kiracı imza etmekle: Aracın hasarlarını incelemiş, yeni oluşacak hasarlardan sorumlu olduğunu kabul eder.").FontSize(7);
                        r.RelativeItem().BorderVertical(0.75f).BorderRight(0.75f).BorderColor(Line).Padding(4).Text(
                            "Kiracı imza etmekle: Kiralayanın Standart Kiralama Koşullarını ve sözleşmenin arka yüzündeki hususları tam anlamıyla kabul ettiğini beyan eder.\n" +
                            "By signing the lessee accepts the Lessor's Standard Lease terms and the points stated on the reverse.").FontSize(7);
                    });

                    // ========== 3 İMZA BLOĞU ==========
                    col.Item().Row(r =>
                    {
                        void Imza(IContainer box, string rol, string? ad)
                            => box.Border(0.75f).BorderColor(Line).Padding(5).Column(c =>
                            {
                                c.Item().Text(rol).FontSize(7.5f).Bold();
                                c.Item().Text(ad ?? "").FontSize(8);
                                c.Item().Height(26);
                                c.Item().Text("İMZA - SİGNATURE").FontSize(7.5f).Bold();
                            });
                        r.RelativeItem().Element(b => Imza(b, "ARACI TESLİM EDEN / DELIVERED BY", s.TeslimAlanAd));
                        r.RelativeItem().Element(b => Imza(b, "1. SÜRÜCÜ / 1st DRIVER", s.MusteriAd));
                        r.RelativeItem().Element(b => Imza(b, "2. SÜRÜCÜ / 2nd DRIVER", s.IkinciSurucuAd));
                    });
                });
            });
        }).GeneratePdf();

    private static IContainer LabelCell(IContainer c) => c.Border(0.5f).BorderColor(Line).Background(LabelBg).PaddingHorizontal(3).PaddingVertical(2.5f);
    private static IContainer ValCell(IContainer c) => c.Border(0.5f).BorderColor(Line).PaddingHorizontal(3).PaddingVertical(2.5f);

    public byte[] Invoice(Invoice inv) =>
        Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(40);
                p.Header().Text("Fatura").FontSize(18).SemiBold();
                p.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(6);
                    col.Item().Text($"Fatura No: {inv.No}");
                    col.Item().Text($"Tarih: {inv.Tarih:yyyy-MM-dd}");
                    col.Item().PaddingTop(6).Text("Kalemler").SemiBold();
                    foreach (var l in inv.Lines)
                        col.Item().Text($"  • {l.Aciklama}  ×{l.Miktar:N2}  = {l.SatirToplam:N2}");
                    col.Item().PaddingTop(6).Text($"Net: {inv.NetTutar:N2}    KDV: {inv.KdvTutar:N2}");
                    col.Item().Text($"Genel Toplam: {inv.GenelToplam:N2} {inv.Currency}").SemiBold();
                });
                p.Footer().AlignCenter().Text($"RentPro — {inv.No}").FontSize(9);
            });
        }).GeneratePdf();
}
