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
            string sheet;
            IReadOnlyList<string>? headers = null;
            IEnumerable<IReadOnlyList<object?>> rows = [];

            switch (liste)
            {
                case "araclar":
                    sheet = "Araclar";
                    headers = ["Plaka", "Marka", "Grup", "Şube", "Durum", "KM"];
                    rows = (await vs.ListAsync()).Select(v =>
                        (IReadOnlyList<object?>)new object?[] { v.Plaka, v.Marka, v.Grup, v.Sube, v.Durum.ToString(), v.Km }).ToList();
                    break;
                case "cariler":
                    sheet = "Cariler";
                    headers = ["Ad", "Tip", "Telefon", "E-posta"];
                    rows = (await cs.ListAsync()).Select(c =>
                        (IReadOnlyList<object?>)new object?[] { c.DisplayName, c.Tip.ToString(), c.CepTel, c.Email }).ToList();
                    break;
                case "faturalar":
                    sheet = "Faturalar";
                    headers = ["No", "Tarih", "Net", "KDV", "Toplam", "Durum"];
                    rows = (await inv.ListAsync()).Select(f =>
                        (IReadOnlyList<object?>)new object?[] { f.No, f.Tarih.ToString("yyyy-MM-dd"), f.NetTutar, f.KdvTutar, f.GenelToplam, f.Durum.ToString() }).ToList();
                    break;
                default:
                    return Results.NotFound();
            }

            var csv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
            var bytes = csv ? ex.Csv(headers, rows) : ex.Xlsx(sheet, headers, rows);
            var ct = csv ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            return Results.File(bytes, ct, $"{liste}.{(csv ? "csv" : "xlsx")}");
        });

        return app;
    }
}
