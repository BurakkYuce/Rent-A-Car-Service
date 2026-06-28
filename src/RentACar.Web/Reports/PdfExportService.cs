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

    public byte[] Contract(RentalContract c) =>
        Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(40);
                p.Header().Text("Kira Sözleşmesi").FontSize(18).SemiBold();
                p.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(6);
                    col.Item().Text($"Sözleşme No: {c.SozlesmeNo}");
                    col.Item().Text($"Başlangıç: {c.BasTar:yyyy-MM-dd}    Bitiş: {c.BitTar:yyyy-MM-dd}");
                    col.Item().Text($"Tutar: {c.Tutar:N2}    Genel Toplam: {c.GenelToplam:N2}");
                    col.Item().Text($"Bakiye: {c.Bakiye:N2}").SemiBold();
                });
                p.Footer().AlignCenter().Text($"TürevRent — {c.SozlesmeNo}").FontSize(9);
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
                p.Footer().AlignCenter().Text($"TürevRent — {inv.No}").FontSize(9);
            });
        }).GeneratePdf();
}
