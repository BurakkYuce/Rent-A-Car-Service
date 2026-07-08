using RentACar.Application.AracKredileri;
using RentACar.Application.AracSiparisleri;
using RentACar.Application.Authorization;
using RentACar.Application.Baflar;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.DropTanimlari;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Locations;
using RentACar.Application.Penalties;
using RentACar.Application.Personnel;
using RentACar.Application.VehicleSales;
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
            VehicleService vs, CustomerService cs, InvoiceService inv,
            PenaltyService ps, ExpenseService es, CashService cash,
            VehicleSaleService vss, AracSiparisService asp, AracKrediService akr, BafService baf,
            RentalService rs, ReservationService rez, LocationService loc, DropTanimService drop, ReportExportService ex) =>
        {
            // Sütun tanımları test-edilebilir katalogda (ListExportCatalog); endpoint yalnız dispatch eder.
            ExportTable? t = liste switch
            {
                "araclar" => ListExportCatalog.Araclar(await vs.ListAsync()),
                "cariler" => ListExportCatalog.Cariler(await cs.ListAsync()),
                "faturalar" => ListExportCatalog.Faturalar(await inv.ListAsync()),
                "cezalar" => ListExportCatalog.Cezalar(await ps.ListAsync()),
                "giderler" => ListExportCatalog.Giderler(await es.ListAsync()),
                "nakit-islemler" => ListExportCatalog.NakitIslemler(await cash.ListAsync()),
                "arac-satislari" => ListExportCatalog.AracSatislari(await vss.ListAsync()),
                "arac-siparisleri" => ListExportCatalog.AracSiparisleri(await asp.ListAsync()),
                "arac-kredileri" => ListExportCatalog.AracKredileri(await akr.ListAsync()),
                "baflar" => ListExportCatalog.Baflar(await baf.ListAsync()),
                "kiralar" => ListExportCatalog.Kiralar(await rs.SearchAsync(new RentalFilter())),
                "rezervasyonlar" => ListExportCatalog.Rezervasyonlar(await rez.ListAsync()),
                "lokasyonlar" => ListExportCatalog.Lokasyonlar(await loc.ListAsync()),
                "drop-tanimlari" => ListExportCatalog.DropTanimlari(await drop.ListAsync()),
                _ => null
            };
            if (t is null) return Results.NotFound();

            var csv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
            var bytes = csv ? ex.Csv(t.Headers, t.Rows) : ex.Xlsx(t.Sheet, t.Headers, t.Rows);
            var ct = csv ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            return Results.File(bytes, ct, $"{liste}.{(csv ? "csv" : "xlsx")}");
        });

        // Personel export AYRI grup — HASSAS PII (TC + maaş) → ManageUsers (Admin) gate'i (ViewReports YETMEZ).
        // KVKK: docs/ops/kvkk-export-notu.md. decrypt cipher'ları bellekte çözer; erişim Serilog request-log'unda izlenir.
        var personel = app.MapGroup("/listeler/export-personel").RequirePermission(Permission.ManageUsers);
        personel.MapGet("/", async (PersonelService ps, ISecretProtector secrets, ReportExportService ex, string? format) =>
        {
            var t = ListExportCatalog.Personel(await ps.ListAsync(), c => secrets.Unprotect(c));
            var csv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
            var bytes = csv ? ex.Csv(t.Headers, t.Rows) : ex.Xlsx(t.Sheet, t.Headers, t.Rows);
            var ct = csv ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            return Results.File(bytes, ct, $"personel.{(csv ? "csv" : "xlsx")}");
        });

        return app;
    }
}
