using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Rapor;

/// <summary>
/// Dört rolün açtığı operasyon raporları (OperationsWrite VEYA ViewReports): araç durum takip, km detay, periyodik
/// servis, sigorta-muayene, karşılaştırmalı analiz. Blazor bu sayfalarda şube kapsamı UYGULAMIYORDU (operatör tüm
/// filoyu görüyordu); API kapsamı uygular (<see cref="ReportScope"/>).
/// </summary>
public static partial class ReportApi
{
    private static void MapOperations(RouteGroupBuilder ops)
    {
        ops.MapGet("/arac-durum-takip", VehicleStatusTracking);
        ops.MapGet("/km-detay", MileageDetail);
        ops.MapGet("/periyodik-servis", PeriodicService);
        ops.MapGet("/sigorta-muayene", InsuranceInspection);
        ops.MapGet("/karsilastirmali-analiz", ComparativeAnalysis);
        MapStaffShifts(ops);
    }

    // ------------------------------------------------------------------ araç durum takip

    public sealed record VehicleTrackingReport(string Gorunum, IReadOnlyList<AracDurumTakipRow> Gunler);

    /// <summary>
    /// Araç durum takip. <c>gorunum=gun</c> (varsayılan): gün başına dolu/bakım/boş (özet alanında, tüm günler);
    /// <c>gorunum=arac</c>: araç başına gün kovaları (sayfalı satırlar). Dönem gün tavanı 366; boş dönem = son 30 gün.
    /// Şube süzgeci kapsamlı kullanıcıda KENDİ şubesine zorlanır.
    /// </summary>
    private static async Task<Ok<ReportResult<VehicleTrackingReport, AracDurumTakipAracRow>>> VehicleStatusTracking(
        [AsParameters] ReportPeriodQuery q, string? gorunum, string? sube, string? aracSahibi, string? grup, string? sipp,
        string? plaka, [AsParameters] ReportPageQuery page, ReportService reports, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, HttpContext http, CancellationToken ct)
    {
        var p = q.Validate(maxDays: 366);
        var g = F(gorunum)?.ToLowerInvariant() ?? "gun";
        if (g is not "gun" and not "arac")
            throw new ValidationException("Geçersiz gorunum. İzin verilenler: gun, arac.", "gorunum");
        if (p.Bas is null != (p.Bit is null))
            throw new ValidationException("Başlangıç ve bitiş birlikte verilmelidir.", p.Bas is null ? "bas" : "bit");
        var filter = new AracDurumTakipFilter
        {
            Sube = await ReportScope.BranchFilterAsync(user, sube, dbf, ct),
            AracSahibi = F(aracSahibi), Grup = F(grup), Sipp = F(sipp), Plaka = F(plaka),
        };
        IReadOnlyList<AracDurumTakipRow> gunler = [];
        IReadOnlyList<AracDurumTakipAracRow> vehicles = [];
        if (g == "arac") vehicles = await reports.GetVehicleStatusTrackingByVehicleAsync(filter, p.FromAnchor, p.Bas is null ? null : ReportPeriod.Anchor(p.Bit!.Value), ct);
        else gunler = await reports.GetVehicleStatusTrackingAsync(filter, p.FromAnchor, p.Bas is null ? null : ReportPeriod.Anchor(p.Bit!.Value), ct);
        var export = ReportExport.Links(http, user, g == "arac" ? "arac-durum-takip-arac" : "arac-durum-takip",
        [
            .. ReportExport.Period(p), ("sube", filter.Sube), ("aracSahibi", aracSahibi), ("grup", grup), ("sipp", sipp),
            ("plaka", plaka),
        ]);
        return TypedResults.Ok(new ReportResult<VehicleTrackingReport, AracDurumTakipAracRow>(p.ToDto(),
            new VehicleTrackingReport(g, gunler), page.Apply(vehicles, TrackingMap), export));
    }

    private static readonly SortFieldMap<AracDurumTakipAracRow> TrackingMap = SortFieldMap<AracDurumTakipAracRow>
        .Create(r => r.VehicleId).Alan("plaka", r => r.Plaka).Alan("doluGun", r => r.DoluGun)
        .Alan("bosGun", r => r.BosGun).Alan("bakimGun", r => r.BakimGun).Alan("sube", r => r.Sube);

    // ------------------------------------------------------------------ km detay

    public sealed record MileageSummary(int KatedilenKm, int FazlaKm, decimal FazlaKmBedeli);

    /// <summary>Dönmüş kiraların km'si (dönem = kira tarihi). Kapsamlı kullanıcı yalnız kendi şubesinin kiralarını görür.</summary>
    private static async Task<Ok<ReportResult<MileageSummary, KmDetayRow>>> MileageDetail(
        [AsParameters] ReportPeriodQuery q, [AsParameters] ReportPageQuery page, ReportService reports,
        ICurrentUser user, IDbContextFactory<AppDbContext> dbf, HttpContext http, CancellationToken ct)
    {
        var p = q.Validate();
        var rowList = await reports.GetKmDetailAsync(p.FromUtc, p.ToUtc, ct);
        var scope = await ReportScope.RentalsInScopeAsync(user, rowList.Select(r => r.RentalId), dbf, ct);
        var rows = scope is null ? rowList.ToList() : rowList.Where(r => scope.Contains(r.RentalId)).ToList();
        return TypedResults.Ok(new ReportResult<MileageSummary, KmDetayRow>(p.ToDto(),
            new MileageSummary(rows.Sum(r => r.KatedilenKm), rows.Sum(r => r.FazlaKm), rows.Sum(r => r.FazlaKmBedeli)),
            page.Apply(rows, MileageMap), ReportExport.Links(http, user, "km-detay", ReportExport.Period(p))));
    }

    private static readonly SortFieldMap<KmDetayRow> MileageMap = SortFieldMap<KmDetayRow>
        .Create(r => r.RentalId).Alan("sozlesmeNo", r => r.SozlesmeNo).Alan("plaka", r => r.Plaka)
        .Alan("katedilenKm", r => r.KatedilenKm).Alan("fazlaKm", r => r.FazlaKm).Alan("basTar", r => r.BasTar);

    // ------------------------------------------------------------------ periyodik servis

    /// <summary>Km bazlı bakım uyarısı. <c>esik</c>: yalnız kalan km'si bu değerin altındakiler (0–10.000.000).</summary>
    private static async Task<Ok<ReportResult<ReportCount, PeriyodikServisRow>>> PeriodicService(
        string? plaka, string? sube, bool? aktif, int? esik, [AsParameters] ReportPageQuery page, ReportService reports,
        ICurrentUser user, IDbContextFactory<AppDbContext> dbf, HttpContext http, CancellationToken ct)
    {
        if (esik is < 0 or > 10_000_000)
            throw new ValidationException("Eşik 0 ile 10.000.000 km arasında olmalıdır.", "esik");
        var effectiveBranch = await ReportScope.BranchFilterAsync(user, sube, dbf, ct);
        var rows = await reports.GetPeriodicServiceAsync(new PeriyodikServisFilter
        {
            Plaka = F(plaka), Sube = effectiveBranch, Aktif = aktif, UyariEsigi = esik,
        }, ct);
        return TypedResults.Ok(new ReportResult<ReportCount, PeriyodikServisRow>(new ReportPeriodDto(null, null),
            new ReportCount(rows.Count), page.Apply(rows, PeriodicMap),
            ReportExport.Links(http, user, "periyodik-servis", [])));
    }

    private static readonly SortFieldMap<PeriyodikServisRow> PeriodicMap = SortFieldMap<PeriyodikServisRow>
        .Create(r => r.VehicleId).Alan("plaka", r => r.Plaka).Alan("kalanKm", r => r.KalanKm)
        .Alan("guncelKm", r => r.GuncelKm).Alan("sube", r => r.Sube);
}
