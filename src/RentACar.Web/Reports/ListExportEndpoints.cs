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
using RentACar.Application.Legal;
using RentACar.Application.Locations;
using RentACar.Application.Penalties;
using RentACar.Application.Personnel;
using RentACar.Application.Regulation;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
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

        grp.MapGet("/{liste}", async (string liste, string? format, HttpRequest req,
            VehicleService vs, CustomerService cs, InvoiceService inv,
            PenaltyService ps, ExpenseService es, CashService cash,
            VehicleSaleService vss, AracSiparisService asp, AracKrediService akr, BafService baf,
            RentalService rs, ReservationService rez, LocationService loc, DropTanimService drop,
            FiloKiralamaService fks, VadeService vade, HukukDosyaService hukuk,
            ReportExportService ex, PdfExportService pdf) =>
        {
            // Cari ekstre parametreli (cariId) → switch dışında; carinin defter satır-detayı + yürüyen bakiye.
            if (liste == "cari-ekstre")
            {
                var cariId = FormParse.Id(req.Query["cariId"].ToString());
                if (cariId is null) return Results.BadRequest("cariId gerekli.");
                // FAZ-65: ekrandaki filtre export'a AYNEN taşınır (gördüğün = indirdiğin).
                var ekstre = await cash.GetStatementAsync(cariId.Value, new CariEkstreFilter
                {
                    Bas = FormParse.Date(req.Query["bas"].ToString()),
                    Bit = FormParse.Date(req.Query["bit"].ToString())?.AddDays(1).AddTicks(-1),
                    Doviz = NullIfEmpty(req.Query["doviz"].ToString()),
                    SourceType = NullIfEmpty(req.Query["kaynak"].ToString()),
                    KiraDurum = Enum.TryParse<RentalStatus>(req.Query["kiraDurum"].ToString(), out var kd) ? kd : null
                });
                return ExportFile(ListExportCatalog.CariEkstre(ekstre.Satirlar, ekstre.Devir), format, ex, pdf, "cari-ekstre");
            }

            // Sütun tanımları test-edilebilir katalogda (ListExportCatalog); endpoint yalnız dispatch eder.
            ExportTable? t = liste switch
            {
                "araclar" => ListExportCatalog.Araclar(await vs.ListAsync()),
                "cariler" => ListExportCatalog.Cariler(await cs.ListAsync()),
                "faturalar" => ListExportCatalog.Faturalar(await inv.ListAsync(), MusteriResolver(await cs.ListAsync())),
                "cezalar" => ListExportCatalog.Cezalar(await ps.ListAsync()),
                // FAZ-63: ekrandaki arama export'a AYNEN taşınır (gördüğün = indirdiğin).
                // Şube kapsamı servis içinde ayrıca uygulanır — filtre onu genişletemez.
                "giderler" => ListExportCatalog.Giderler(await es.ListAsync(new ExpenseFilter
                {
                    Ara = NullIfEmpty(req.Query["ara"].ToString()),
                    Plaka = NullIfEmpty(req.Query["plaka"].ToString()),
                    CariId = FormParse.Id(req.Query["cariId"].ToString()),
                    Tip = Enum.TryParse<ExpenseType>(req.Query["tip"].ToString(), out var gt) ? gt : null,
                    Sube = NullIfEmpty(req.Query["sube"].ToString()),
                    Bas = FormParse.Date(req.Query["bas"].ToString()),
                    Bit = FormParse.Date(req.Query["bit"].ToString())?.AddDays(1).AddTicks(-1)
                })),
                // FAZ-52: fatura satır detayı — ekrandaki filtre export'a AYNEN taşınır.
                "fatura-detaylari" => ListExportCatalog.FaturaDetaylari(await inv.ListLinesAsync(
                    new RentACar.Application.Finance.FaturaSatirFilter
                    {
                        CariId = FormParse.Id(req.Query["cariId"].ToString()),
                        Ara = NullIfEmpty(req.Query["ara"].ToString()),
                        Plaka = NullIfEmpty(req.Query["plaka"].ToString()),
                        Ofis = NullIfEmpty(req.Query["ofis"].ToString()),
                        Bas = FormParse.Date(req.Query["bas"].ToString()),
                        Bit = FormParse.Date(req.Query["bit"].ToString())?.AddDays(1).AddTicks(-1),
                        IptalleriGizle = req.Query["iptal"].ToString() == "gizle"
                    })),
                "nakit-islemler" => ListExportCatalog.NakitIslemler(await cash.ListAsync()),
                "arac-satislari" => ListExportCatalog.AracSatislari(await vss.ListAsync()),
                // FAZ-17: ekrandaki filtre export'a AYNEN taşınır (giderler/AracKredi deseni) —
                // kullanıcı gördüğü listeyi indirir, sessizce tüm tabloyu değil.
                "arac-siparisleri" => ListExportCatalog.AracSiparisleri(
                    await asp.SearchAsync(new AracSiparisFilter
                    {
                        CariId = FormParse.Id(req.Query["cariF"].ToString()),
                        Ara = NullIfEmpty(req.Query["araF"].ToString()),
                        Arac = NullIfEmpty(req.Query["aracF"].ToString()),
                        DosyaNo = NullIfEmpty(req.Query["dosyaF"].ToString()),
                        Durum = Enum.TryParse<SiparisDurum>(req.Query["durumF"].ToString(), out var sd) ? sd : null,
                        Bas = FormParse.Date(req.Query["bas"].ToString()),
                        Bit = FormParse.Date(req.Query["bit"].ToString())?.AddDays(1).AddTicks(-1)
                    }),
                    MusteriResolver(await cs.ListAsync()), KrediResolver(await akr.ListAsync())),
                // FAZ-13: ekrandaki filtre export'a AYNEN taşınır (giderler deseni) — kullanıcı
                // gördüğü listeyi indirir, sessizce tüm tabloyu değil.
                "arac-kredileri" => ListExportCatalog.AracKredileri(
                    await akr.SearchAsync(new AracKrediFilter
                    {
                        CariId = FormParse.Id(req.Query["cariF"].ToString()),
                        Plaka = NullIfEmpty(req.Query["plakaF"].ToString()),
                        DosyaNo = NullIfEmpty(req.Query["dosyaF"].ToString()),
                        Durum = Enum.TryParse<KrediDurum>(req.Query["durumF"].ToString(), out var kd) ? kd : null,
                        Bas = FormParse.Date(req.Query["bas"].ToString()),
                        Bit = FormParse.Date(req.Query["bit"].ToString())?.AddDays(1).AddTicks(-1)
                    }),
                    MusteriResolver(await cs.ListAsync()), PlakaResolver(await vs.ListAsync())),
                "baflar" => ListExportCatalog.Baflar(await baf.ListAsync()),
                "kiralar" => ListExportCatalog.Kiralar(await rs.SearchAsync(new RentalFilter())),
                "rezervasyonlar" => ListExportCatalog.Rezervasyonlar(await rez.ListAsync()),
                "lokasyonlar" => ListExportCatalog.Lokasyonlar(await loc.ListAsync()),
                "drop-tanimlari" => ListExportCatalog.DropTanimlari(await drop.ListAsync()),
                "filo-kiralama" => ListExportCatalog.FiloKiralamalar(await fks.ListAsync(),
                    PlakaResolver(await vs.ListAsync()), MusteriResolver(await cs.ListAsync())),
                "vade" => ListExportCatalog.Vadeler(await vade.GetAllAsync(ct: default),
                    PlakaResolver(await vs.ListAsync())),
                // FAZ-41: ekrandaki süzgeç export'a AYNEN taşınır (gördüğün = indirdiğin).
                "hukuk" => ListExportCatalog.HukukDosyalari(await hukuk.SearchAsync(new HukukDosyaFilter
                {
                    CariId = FormParse.Id(req.Query["cariId"].ToString()),
                    Bas = FormParse.Date(req.Query["bas"].ToString()),
                    Bit = FormParse.Date(req.Query["bit"].ToString())?.AddDays(1).AddTicks(-1),
                    FaturaNo = NullIfEmpty(req.Query["faturaNo"].ToString()),
                    DosyaNo = NullIfEmpty(req.Query["dosyaNo"].ToString()),
                    Ara = NullIfEmpty(req.Query["ara"].ToString()),
                    Tur = Enum.TryParse<HukukTuru>(req.Query["tur"].ToString(), out var ht) ? ht : null,
                    Durum = Enum.TryParse<HukukDurum>(req.Query["durum"].ToString(), out var hd) ? hd : null
                })),
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

    /// <summary>Boş/whitespace sorgu değeri → null (filtre alanı "verilmemiş" sayılsın).</summary>
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

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

    /// <summary>FAZ-17: sipariş export'unda Kredi_No kolonu — kredi Id'si yerine "KR-000001 — Banka".</summary>
    private static Func<Guid, string?> KrediResolver(IReadOnlyList<AracKredi> krediler)
    {
        var d = krediler.ToDictionary(x => x.Id, x => (string?)$"{x.No} — {x.BankaAdi}");
        return id => d.TryGetValue(id, out var n) ? n : null;
    }
}
