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

    /// <summary>Kira sözleşmesi PDF'i — HTML-print ile AYNI SozlesmeView'den (içerik tek kaynak; adversarial
    /// inceleme 5c). Ekspertiz diyagramı PDF'te kutu-bazlı (SVG değil — bilinçli sunum farkı, içerik paritesi korunur).</summary>
    public byte[] Contract(RentACar.Application.Bookings.SozlesmeView s) =>
        Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(36);
                p.Header().Row(r =>
                {
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text("ARAÇ TESLİM BELGESİ / RENTAL AGREEMENT").FontSize(13).SemiBold();
                        if (s.FirmaUnvan is not null) c.Item().Text(s.FirmaUnvan).FontSize(10).SemiBold();
                        if (s.FirmaTel is not null) c.Item().Text($"Tel: {s.FirmaTel}").FontSize(9);
                        if (s.FirmaAdres is not null) c.Item().Text(s.FirmaAdres).FontSize(9);
                        if (s.FirmaVergiNo is not null) c.Item().Text($"{s.FirmaVergiDairesi} VD. {s.FirmaVergiNo}").FontSize(9);
                    });
                    r.ConstantItem(120).AlignRight().Text(s.SozlesmeNo).FontSize(15).Bold();
                });
                p.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(4);
                    var pb = string.Equals(RentACar.Application.Kur.KurService.NormalizeKod(s.Doviz), "TRY",
                        StringComparison.OrdinalIgnoreCase) ? "TL" : (s.Doviz ?? "TL");

                    col.Item().Text("Müşteri / Sürücü").FontSize(11).SemiBold();
                    col.Item().Text(s.MusteriAd).SemiBold();
                    if (s.TcKimlik is not null) col.Item().Text($"T.C. Kimlik / Pasaport: {s.TcKimlik}").FontSize(9);
                    if (s.EhliyetNo is not null)
                        col.Item().Text($"Ehliyet: {s.EhliyetNo} {s.EhliyetSinifi} {s.EhliyetYeri} {s.EhliyetTarihi:dd.MM.yyyy}".Trim()).FontSize(9);
                    if (s.DogumTarihi is not null) col.Item().Text($"Doğum Tarihi: {s.DogumTarihi:dd.MM.yyyy}").FontSize(9);
                    if (s.MusteriTel is not null) col.Item().Text($"Tel: {s.MusteriTel}").FontSize(9);
                    if (s.MusteriAdres is not null) col.Item().Text($"Adres: {s.MusteriAdres}").FontSize(9);
                    if (s.IkinciSurucuAd is not null)
                    {
                        col.Item().PaddingTop(2).Text($"2. Sürücü: {s.IkinciSurucuAd}").FontSize(9).SemiBold();
                        if (s.IkinciTcKimlik is not null) col.Item().Text($"  TC: {s.IkinciTcKimlik}").FontSize(9);
                        if (s.IkinciEhliyetNo is not null)
                            col.Item().Text($"  Ehliyet: {s.IkinciEhliyetNo} {s.IkinciEhliyetSinifi} {s.IkinciEhliyetYeri} {s.IkinciEhliyetTarihi:dd.MM.yyyy}".Trim()).FontSize(9);
                        if (s.IkinciDogumTarihi is not null) col.Item().Text($"  Doğum: {s.IkinciDogumTarihi:dd.MM.yyyy}").FontSize(9);
                    }

                    col.Item().PaddingTop(4).Text("Araç").FontSize(11).SemiBold();
                    col.Item().Text($"{s.Plaka} {s.Marka} {s.Tip}{(s.ModelYili is null ? "" : $" ({s.ModelYili})")} — Grup: {s.Grup ?? "—"} · Yakıt: {s.Yakit}").FontSize(9);
                    col.Item().Text($"Kiralandığı Yer: {s.CikisOfisi ?? "—"}{(s.DonusOfisi is null ? "" : $" → {s.DonusOfisi}")}").FontSize(9);

                    col.Item().PaddingTop(4).Text($"Başlangıç: {s.BasTar.LocalDateTime:dd.MM.yyyy HH:mm}    Bitiş: {s.BitTar.LocalDateTime:dd.MM.yyyy HH:mm}    Gün: {s.Gun}").FontSize(9);
                    col.Item().Text($"Çıkış KM/Yakıt: {s.CikisKm?.ToString() ?? "—"}/{s.CikisYakit?.ToString() ?? "—"}    Dönüş KM/Yakıt: {s.DonusKm?.ToString() ?? "—"}/{s.DonusYakit?.ToString() ?? "—"}    Kullanılan KM: {s.KullanilanKm?.ToString() ?? "—"}").FontSize(9);
                    col.Item().Text($"KM Limit: {(s.KmLimit == 0 ? "sınırsız" : s.KmLimit.ToString())}    Aşım Ücreti: {s.FazlaKmUcret:N2} {pb}{(s.KmHediye is null ? "" : $"    KM Hediye: {s.KmHediye}")}").FontSize(9);
                    if (s.BitisSebebi is not null || s.TeslimAlanAd is not null)
                        col.Item().Text($"Bitiş Sebebi: {s.BitisSebebi ?? "—"}    Teslim Alan: {s.TeslimAlanAd ?? "—"}").FontSize(9);
                    col.Item().Text($"Günlük: {s.GunlukUcret:N2}    Kira: {s.Tutar:N2}    Fazla KM: {s.FazlaKmBedeli:N2}    Yakıt: {s.YakitBedeli:N2}    Uzatma: {s.UzatmaBedeli:N2} ({pb})").FontSize(9);
                    if (s.EkHizmetler.Count > 0)
                        col.Item().Text($"Ek Hizmetler: {string.Join(" · ", s.EkHizmetler.Select(e => $"{e.Ad}: {e.Toplam:N2}"))} (toplam {s.EkHizmetToplam:N2} {pb})").FontSize(9);
                    col.Item().Text($"Genel Toplam: {s.GenelToplam:N2} {pb}    Tahsilat: {s.Tahsilat:N2} {pb}    Bakiye: {s.Bakiye:N2} {pb}").FontSize(10).SemiBold();

                    // Ekspertiz (çıkış + dönüş) — kutu-bazlı ızgara
                    col.Item().PaddingTop(6).Row(r =>
                    {
                        static void Ekspertiz(IContainer box, string baslik, int? yakit)
                            => box.Border(1).Padding(6).Column(e =>
                            {
                                e.Spacing(3);
                                var dolu = Math.Clamp(yakit ?? 0, 0, 12);
                                e.Item().Text(baslik).FontSize(9).SemiBold();
                                e.Item().Text($"Yakıt: {yakit?.ToString() ?? "—"}/12   E [{new string('#', dolu)}{new string('.', 12 - dolu)}] F").FontSize(8);
                                e.Item().Text("[ ] Avadanlık   [ ] Trafik Seti   [ ] Stepne   [ ] Zincir").FontSize(8);
                                e.Item().Text("Hasar notu: ______________________________").FontSize(8);
                            });
                        r.RelativeItem().Element(b => Ekspertiz(b, "ARAÇ ÇIKIŞ EKSPERTİZİ", s.CikisYakit));
                        r.ConstantItem(8);
                        r.RelativeItem().Element(b => Ekspertiz(b, "ARAÇ DÖNÜŞ EKSPERTİZİ", s.DonusYakit));
                    });

                    col.Item().PaddingTop(6).Text("Kiracı, aracı ve mevcut hasarları kontrol etmiş olup yeni oluşacak hasarlardan sorumludur. İmza ile kiracı, Kiralayanın Standart Kiralama Koşullarını kabul ettiğini beyan eder. / By signing, the renter accepts the Lessor's Standard Rental Terms.").FontSize(8).Italic();
                    col.Item().PaddingTop(14).Row(r =>
                    {
                        void Imza(IContainer col2, string etiket)
                            => col2.Column(c2 => { c2.Item().Text(etiket).FontSize(9); c2.Item().PaddingTop(20).LineHorizontal(1); });
                        r.RelativeItem().Element(b => Imza(b, $"ARACI TESLİM EDEN{(s.TeslimAlanAd is null ? "" : $" — {s.TeslimAlanAd}")}"));
                        r.ConstantItem(18);
                        r.RelativeItem().Element(b => Imza(b, $"1. SÜRÜCÜ — {s.MusteriAd}"));
                        if (s.IkinciSurucuAd is not null)
                        {
                            r.ConstantItem(18);
                            r.RelativeItem().Element(b => Imza(b, $"2. SÜRÜCÜ — {s.IkinciSurucuAd}"));
                        }
                    });
                });
                p.Footer().AlignCenter().Text($"{s.FirmaUnvan ?? "RentPro"} — {s.SozlesmeNo}").FontSize(9);
            });
        }).GeneratePdf();

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
