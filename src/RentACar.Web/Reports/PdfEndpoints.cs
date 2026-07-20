using RentACar.Application.Authorization;
using RentACar.Application.BelgeSablon;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Reports;

/// <summary>PDF indirme uçları (roadmap F4 + PR-C): kira sözleşmesi + fatura + tahsilat/ödeme makbuzu.
/// Salt-okur GET (antiforgery gerekmez). Fatura/makbuz TenantSettings'ten firma markası + logo taşır.</summary>
public static class PdfEndpoints
{
    /// <summary>TenantSettings'ten PDF marka bilgisi (logo + firma) kur.</summary>
    private static async Task<PdfMarka> MarkaAsync(TenantSettingsService ts, CancellationToken ct)
    {
        var s = await ts.GetAsync(ct);
        return new PdfMarka(s.LogoBytes, s.FirmaUnvan, s.FirmaMarka, s.FirmaAdres, s.FirmaTel, s.FirmaVergiDairesi, s.FirmaVergiNo);
    }

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

        var finans = app.MapGroup("/faturalar").RequirePermission(Permission.FinanceWrite);
        finans.MapGet("/{id:guid}/pdf", async (Guid id, InvoiceService svc, CustomerService cs,
            TenantSettingsService ts, BelgeSablonCozumleyici sablon, PdfExportService pdf, string? indir, CancellationToken ct) =>
        {
            var inv = await svc.GetAsync(id, ct);
            if (inv is null) return Results.NotFound();
            var cari = await cs.GetAsync(inv.CariId, ct);
            var bytes = pdf.Invoice(inv, await MarkaAsync(ts, ct), cari?.DisplayName,
                await sablon.VarsayilanAsync(BelgeTuru.Fatura, ct));
            return indir == "1"
                ? Results.File(bytes, "application/pdf", $"{inv.No}.pdf")
                : Results.File(bytes, "application/pdf");
        });

        // PR-C: tahsilat/ödeme makbuzu PDF (CashTransaction'dan; markalı).
        app.MapGroup("/kasa").RequirePermission(Permission.FinanceWrite)
            .MapGet("/makbuz/{id:guid}/pdf", async (Guid id, CashService cash, CustomerService cs,
                TenantSettingsService ts, BelgeSablonCozumleyici sablon, PdfExportService pdf, string? indir, CancellationToken ct) =>
            {
                var tx = await cash.GetAsync(id, ct);
                if (tx is null) return Results.NotFound();
                var cari = await cs.GetAsync(tx.CariId, ct);
                var bytes = pdf.TahsilatMakbuzu(tx, await MarkaAsync(ts, ct), cari?.DisplayName,
                    await sablon.VarsayilanAsync(BelgeTuru.Makbuz, ct));
                return indir == "1"
                    ? Results.File(bytes, "application/pdf", $"makbuz-{tx.No}.pdf")
                    : Results.File(bytes, "application/pdf");
            });

        return app;
    }
}
