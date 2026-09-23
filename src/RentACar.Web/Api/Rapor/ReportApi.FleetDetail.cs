using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Jobs;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Api.Rapor;

/// <summary>ViewReports filo/operasyon raporları: araç günlük durum, servis özeti, rezervasyon kaynağı, otomatik servisler.</summary>
public static partial class ReportApi
{
    private static void MapFleetDetail(RouteGroupBuilder vr)
    {
        vr.MapGet("/arac-gunluk-durum", VehicleDailyStatus);
        vr.MapGet("/servis-ozet", ServiceCost);
        vr.MapGet("/rezervasyon-kaynak", ReservationSource);
        vr.MapGet("/otomatik-servisler", JobRuns);
    }

    // ------------------------------------------------------------------ araç günlük durum

    public sealed record VehicleDailySummary(DateOnly Gun, int AracAdet, decimal GunlukKira, decimal GunlukHizmet, decimal GunlukToplam);

    /// <summary>Verilen GÜNDE aktif kiraların günlük gelir kesiti (PROJEKSİYON; deftere yazılmaz). Müşteri KVKK kuralıyla.</summary>
    private static async Task<Ok<ReportResult<VehicleDailySummary, AracGunlukDurumRow>>> VehicleDailyStatus(
        DateOnly? gun, string? ofis, string? grup, string? sipp, string? aracSahibi, string? plaka,
        [AsParameters] ReportPageQuery page, ReportService reports, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var g = ReportPeriod.ValidateDay(gun, "gun") ?? ReportPeriod.Today;
        var satirlar = await reports.GetAracGunlukDurumAsync(ReportPeriod.Anchor(g), new AracGunlukDurumFilter
        {
            Ofis = F(ofis), Grup = F(grup), Sipp = F(sipp), AracSahibi = F(aracSahibi), Plaka = F(plaka),
        }, ct);
        var mask = await CustomerMask.LoadAsync(dbf, null, ct);
        var rows = satirlar.Select(r => r with { Musteri = mask.Name(r.Musteri) }).ToList();
        var ozet = new VehicleDailySummary(g, rows.Select(r => r.VehicleId).Distinct().Count(),
            rows.Sum(r => r.GunlukKira), rows.Sum(r => r.GunlukHizmet), rows.Sum(r => r.GunlukToplam));
        return TypedResults.Ok(new ReportResult<VehicleDailySummary, AracGunlukDurumRow>(new ReportPeriodDto(g, g), ozet,
            page.Apply(rows, VehicleDailyMap),
            ReportExport.Links(http, user, "arac-gunluk-durum",
            [
                ("gun", ReportExport.Day(g)), ("ofis", ofis), ("grup", grup), ("sipp", sipp), ("aracSahibi", aracSahibi),
                ("plaka", plaka),
            ])));
    }

    private static readonly SiralamaHaritasi<AracGunlukDurumRow> VehicleDailyMap = SiralamaHaritasi<AracGunlukDurumRow>
        .Olustur(r => r.RentalId).Alan("plaka", r => r.Plaka).Alan("sozlesmeNo", r => r.SozlesmeNo)
        .Alan("gunlukToplam", r => r.GunlukToplam).Alan("basTar", r => r.BasTar);

    // ------------------------------------------------------------------ servis maliyet özeti

    public sealed record ServiceCostRow(Guid VehicleId, string Plaka, string Tip, decimal Toplam, int Adet);

    public sealed record ServiceCostSummary(decimal Toplam, int Adet);

    private static async Task<Ok<ReportResult<ServiceCostSummary, ServiceCostRow>>> ServiceCost(
        [AsParameters] ReportPeriodQuery q, [AsParameters] ReportPageQuery page, ReportService reports,
        ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var rows = (await reports.GetServiceCostSummaryAsync(p.FromUtc, p.ToUtc, ct))
            .Select(r => new ServiceCostRow(r.VehicleId, r.Plaka, r.Tip.ToString(), r.Toplam, r.Adet)).ToList();
        return TypedResults.Ok(new ReportResult<ServiceCostSummary, ServiceCostRow>(p.ToDto(),
            new ServiceCostSummary(rows.Sum(r => r.Toplam), rows.Sum(r => r.Adet)), page.Apply(rows, ServiceCostMap),
            ReportExport.Links(http, user, "servis-ozet", ReportExport.Period(p))));
    }

    private static readonly SiralamaHaritasi<ServiceCostRow> ServiceCostMap = SiralamaHaritasi<ServiceCostRow>
        .Olustur(r => r.VehicleId).Alan("plaka", r => r.Plaka).Alan("tip", r => r.Tip).Alan("toplam", r => r.Toplam)
        .Alan("adet", r => r.Adet);

    // ------------------------------------------------------------------ rezervasyon kaynağı

    private static readonly string[] DateKinds = ["Cikis", "Donus", "Kayit"];

    /// <summary>Kaynak başına adet/gün/ciro. <c>tarihTipi</c>: Cikis (varsayılan) | Donus | Kayit. İptaller varsayılan HARİÇ.</summary>
    private static async Task<Ok<ReportSummaryResult<IReadOnlyList<RezervasyonKaynakRow>>>> ReservationSource(
        [AsParameters] ReportPeriodQuery q, string? tarihTipi, string? ofis, string? grup, bool? iptal,
        ReportService reports, ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var tt = DateKinds.FirstOrDefault(k => string.Equals(k, F(tarihTipi) ?? "Cikis", StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException($"Geçersiz tarihTipi. İzin verilenler: {string.Join(", ", DateKinds)}.", "tarihTipi");
        var rows = await reports.GetRezervasyonKaynakAsync(new RezervasyonKaynakFilter
        {
            Bas = p.FromUtc, Bit = p.ToUtc, TarihTipi = tt, Ofis = F(ofis), Grup = F(grup), IptalleriDahilEt = iptal == true,
        }, ct);
        return TypedResults.Ok(new ReportSummaryResult<IReadOnlyList<RezervasyonKaynakRow>>(p.ToDto(), rows,
            ReportExport.Links(http, user, "rezervasyon-kaynak", ReportExport.Period(p))));
    }

    // ------------------------------------------------------------------ otomatik servisler

    public sealed record JobRunRow(
        Guid Id, string JobAdi, DateTimeOffset BaslangicUtc, DateTimeOffset BitisUtc, int SureMs, bool Basarili,
        int? SonucSayisi, string? Detay);

    /// <summary>Otomatik servis koşu günlüğü (servis tavanı 500) + iş başına son koşu. <c>hatali=true</c> yalnız başarısızlar.</summary>
    private static async Task<Ok<ReportResult<IReadOnlyList<JobRunRow>, JobRunRow>>> JobRuns(
        [AsParameters] ReportPeriodQuery q, string? job, bool? hatali, [AsParameters] ReportPageQuery page,
        JobCalismaLogService logs, ICurrentUser user, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        static JobRunRow Row(Domain.Entities.JobCalismaLog l)
            => new(l.Id, l.JobAdi, l.BaslangicUtc, l.BitisUtc, l.SureMs, l.Basarili, l.SonucSayisi, l.Detay);
        var son = (await logs.SonKosularAsync(ct)).OrderBy(s => s.JobAdi, StringComparer.Ordinal).Select(Row).ToList();
        var rows = (await logs.ListAsync(new JobCalismaLogFilter
        {
            JobAdi = F(job), Bas = p.FromUtc, Bit = p.ToUtc, YalnizHatali = hatali == true ? true : null,
        }, ct)).Select(Row).ToList();
        return TypedResults.Ok(new ReportResult<IReadOnlyList<JobRunRow>, JobRunRow>(p.ToDto(), son,
            page.Apply(rows, JobRunMap), null));
    }

    private static readonly SiralamaHaritasi<JobRunRow> JobRunMap = SiralamaHaritasi<JobRunRow>
        .Olustur(r => r.Id).Alan("baslangic", r => r.BaslangicUtc).Alan("jobAdi", r => r.JobAdi)
        .Alan("sureMs", r => r.SureMs);
}
