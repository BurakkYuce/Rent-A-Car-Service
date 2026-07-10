using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Finance;
using RentACar.Web.Identity;

namespace RentACar.Web.Reports;

/// <summary>PDF indirme uçları (roadmap F4): kira sözleşmesi + fatura. Salt-okur GET (antiforgery gerekmez).</summary>
public static class PdfEndpoints
{
    public static IEndpointRouteBuilder MapPdfEndpoints(this IEndpointRouteBuilder app)
    {
        var kiralar = app.MapGroup("/kiralar").RequirePermission(Permission.OperationsWrite);

        // VARSAYILAN: tarayıcıda GÖRÜNTÜLE (inline — her tıklama indirme klasörünü doldurmasın; oradan
        // yazdırılabilir/kaydedilebilir). ?indir=1 → klasik dosya indirme (Content-Disposition: attachment).
        kiralar.MapGet("/{id:guid}/pdf", async (Guid id, SozlesmeService sozlesme, PdfExportService pdf, string? indir, CancellationToken ct) =>
        {
            var s = await sozlesme.GetAsync(id, ct); // HTML-print ile AYNI view-model (içerik tek kaynak)
            if (s is null) return Results.NotFound();
            var bytes = pdf.Contract(s);
            return indir == "1"
                ? Results.File(bytes, "application/pdf", $"{s.SozlesmeNo}.pdf")
                : Results.File(bytes, "application/pdf");
        });

        // Örnek (şablon) sözleşme PDF'i — gerçek kira gerekmez; RentPro markalı numune (OrnekSozlesme.Ornek()).
        // Gerçek sözleşmeyle AYNI renderer (PdfExportService.Contract) → çıktı formatı tek kaynak.
        kiralar.MapGet("/ornek-sozlesme/pdf", (PdfExportService pdf, string? indir) =>
            indir == "1"
                ? Results.File(pdf.Contract(OrnekSozlesme.Ornek()), "application/pdf", "ornek-sozlesme.pdf")
                : Results.File(pdf.Contract(OrnekSozlesme.Ornek()), "application/pdf"));

        app.MapGroup("/faturalar").RequirePermission(Permission.FinanceWrite)
            .MapGet("/{id:guid}/pdf", async (Guid id, InvoiceService svc, PdfExportService pdf, string? indir, CancellationToken ct) =>
            {
                var inv = await svc.GetAsync(id, ct);
                if (inv is null) return Results.NotFound();
                var bytes = pdf.Invoice(inv);
                return indir == "1"
                    ? Results.File(bytes, "application/pdf", $"{inv.No}.pdf")
                    : Results.File(bytes, "application/pdf");
            });

        return app;
    }
}
