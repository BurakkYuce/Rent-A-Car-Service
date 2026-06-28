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
        app.MapGroup("/kiralar").RequirePermission(Permission.OperationsWrite)
            .MapGet("/{id:guid}/pdf", async (Guid id, IBookingRepository repo, PdfExportService pdf, CancellationToken ct) =>
            {
                var c = await repo.FindRentalAsync(id, ct);
                return c is null ? Results.NotFound() : Results.File(pdf.Contract(c), "application/pdf", $"{c.SozlesmeNo}.pdf");
            });

        app.MapGroup("/faturalar").RequirePermission(Permission.FinanceWrite)
            .MapGet("/{id:guid}/pdf", async (Guid id, InvoiceService svc, PdfExportService pdf, CancellationToken ct) =>
            {
                var inv = await svc.GetAsync(id, ct);
                return inv is null ? Results.NotFound() : Results.File(pdf.Invoice(inv), "application/pdf", $"{inv.No}.pdf");
            });

        return app;
    }
}
