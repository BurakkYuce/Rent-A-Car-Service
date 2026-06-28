using RentACar.Domain.Entities;
using RentACar.Web.Reports;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap F4 — PDF export (QuestPDF). BAĞIMSIZ ORACLE: üretilen byte[] geçerli PDF (%PDF imzası) ve dolu.
/// Birim test (DB yok); Community lisansı PdfExportService static ctor'da set edilir (kimliksiz).
/// </summary>
public sealed class PdfExportTests
{
    private static bool IsPdf(byte[] b)
        => b.Length > 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46; // "%PDF"

    [Fact]
    public void Contract_pdf_is_valid_and_nonempty()
    {
        var c = new RentalContract
        {
            SozlesmeNo = "RZ-000123",
            BasTar = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
            BitTar = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero),
            Tutar = 400m, GenelToplam = 400m, Bakiye = 400m
        };
        var pdf = new PdfExportService().Contract(c);
        Assert.True(pdf.Length > 500);
        Assert.True(IsPdf(pdf));
    }

    [Fact]
    public void Invoice_pdf_is_valid_and_nonempty()
    {
        var inv = new Invoice
        {
            No = "FT-000045",
            Tarih = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            NetTutar = 333.33m, KdvTutar = 66.67m, GenelToplam = 400m, Currency = "TRY"
        };
        inv.Lines.Add(new InvoiceLine { Aciklama = "Araç kirası", Miktar = 1m, SatirToplam = 400m });

        var pdf = new PdfExportService().Invoice(inv);
        Assert.True(pdf.Length > 500);
        Assert.True(IsPdf(pdf));
    }
}
