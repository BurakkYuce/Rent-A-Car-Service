using RentACar.Application.AracKredileri;
using RentACar.Application.AracSiparisleri;
using RentACar.Application.Authorization;
using RentACar.Application.Baflar;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.DropTanimlari;
using RentACar.Application.Expenses;
using RentACar.Application.FiloKiralamalar;
using RentACar.Application.Finance;
using RentACar.Application.Locations;
using RentACar.Application.Penalties;
using RentACar.Application.Personnel;
using RentACar.Application.Regulation;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
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
            RentalService rs, ReservationService rez, LocationService loc, DropTanimService drop,
            FiloKiralamaService fks, VadeService vade, ReportExportService ex, PdfExportService pdf) =>
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
                "filo-kiralama" => ListExportCatalog.FiloKiralamalar(await fks.ListAsync(),
                    PlakaResolver(await vs.ListAsync()), MusteriResolver(await cs.ListAsync())),
                "vade" => ListExportCatalog.Vadeler(await vade.GetAllAsync(ct: default),
                    PlakaResolver(await vs.ListAsync())),
                _ => null
            };
            if (t is null) return Results.NotFound();
            return ExportFile(t, format, ex, pdf, liste);
        });

        // Personel export AYRI grup — HASSAS PII (TC + maaş) → ManageUsers (Admin) gate'i (ViewReports YETMEZ).
        // KVKK: docs/ops/kvkk-export-notu.md. decrypt cipher'ları bellekte çözer; erişim Serilog request-log'unda izlenir.
        var personel = app.MapGroup("/listeler/export-personel").RequirePermission(Permission.ManageUsers);
        personel.MapGet("/", async (PersonelService ps, ISecretProtector secrets, ReportExportService ex, PdfExportService pdf, string? format) =>
        {
            var t = ListExportCatalog.Personel(await ps.ListAsync(), c => secrets.Unprotect(c));
            return ExportFile(t, format, ex, pdf, "personel");
        });

        return app;
    }

    /// <summary>Ortak dispatch: ?format=excel(default)|csv|pdf → ExportTable'ı ilgili byte'a çevirip File döner.
    /// PDF PdfExportService.Table (Excel/CSV ile AYNI veri; generic tablo renderer). TÜM liste export'ları buradan.</summary>
    internal static IResult ExportFile(ExportTable t, string? format, ReportExportService ex, PdfExportService pdf, string ad)
    {
        var fmt = (format ?? "excel").Trim().ToLowerInvariant();
        return fmt switch
        {
            "csv" => Results.File(ex.Csv(t.Headers, t.Rows), "text/csv", $"{ad}.csv"),
            "pdf" => Results.File(pdf.Table(t.Sheet, t.Headers, t.Rows), "application/pdf", $"{ad}.pdf"),
            _ => Results.File(ex.Xlsx(t.Sheet, t.Headers, t.Rows),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{ad}.xlsx")
        };
    }

    // FK→ad çözücüler (filo-kiralama/vade export'ları için): liste bir kez çekilip dict'e alınır.
    private static Func<Guid, string?> PlakaResolver(IReadOnlyList<Vehicle> vehicles)
    {
        var d = vehicles.ToDictionary(x => x.Id, x => (string?)x.Plaka);
        return id => d.TryGetValue(id, out var p) ? p : null;
    }

    private static Func<Guid, string?> MusteriResolver(IReadOnlyList<Customer> customers)
    {
        var d = customers.ToDictionary(x => x.Id, x => (string?)x.DisplayName);
        return id => d.TryGetValue(id, out var n) ? n : null;
    }
}
