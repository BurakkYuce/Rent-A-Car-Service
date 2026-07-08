using RentACar.Application.Authorization;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Web.Identity;

namespace RentACar.Web.Reports;

/// <summary>
/// Liste export uçları (roadmap G6): GET /listeler/export/{liste}?format=excel|csv — araç/cari/fatura
/// listelerini Excel/CSV indirir (ReportExportService deseni). ViewReports. Salt-okur.
/// </summary>
public static class ListExportEndpoints
{
    public static IEndpointRouteBuilder MapListExportEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/listeler/export").RequirePermission(Permission.ViewReports);

        grp.MapGet("/{liste}", async (string liste, string? format,
            VehicleService vs, CustomerService cs, InvoiceService inv, ReportExportService ex) =>
        {
            // Sütun tanımları test-edilebilir katalogda (ListExportCatalog); endpoint yalnız dispatch eder.
            ExportTable? t = liste switch
            {
                "araclar" => ListExportCatalog.Araclar(await vs.ListAsync()),
                "cariler" => ListExportCatalog.Cariler(await cs.ListAsync()),
                "faturalar" => ListExportCatalog.Faturalar(await inv.ListAsync()),
                _ => null
            };
            if (t is null) return Results.NotFound();

            var csv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
            var bytes = csv ? ex.Csv(t.Headers, t.Rows) : ex.Xlsx(t.Sheet, t.Headers, t.Rows);
            var ct = csv ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            return Results.File(bytes, ct, $"{liste}.{(csv ? "csv" : "xlsx")}");
        });

        return app;
    }
}
