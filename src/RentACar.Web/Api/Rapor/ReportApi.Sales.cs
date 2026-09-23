using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Api.Rapor;

/// <summary>Satış/kârlılık raporları: araç kârlılığı (+ boyut özeti), ek hizmet (+ araç pivotu, satır detayı), günlük faaliyet.</summary>
public static partial class ReportApi
{
    private static void MapSales(RouteGroupBuilder vr)
    {
        vr.MapGet("/karlilik", Profitability);
        vr.MapGet("/karlilik/ozet", ProfitabilityByDimension);
        vr.MapGet("/ek-hizmet", AddOnSales);
        vr.MapGet("/ek-hizmet/arac-pivot", AddOnVehiclePivot);
        vr.MapGet("/ek-hizmet/detay", AddOnDetail);
        vr.MapGet("/gunluk", DailyActivity);
    }

    // ------------------------------------------------------------------ kârlılık

    public sealed class ProfitabilityFilter
    {
        [FromQuery(Name = "sube")] public string? Sube { get; set; }
        [FromQuery(Name = "grup")] public string? Grup { get; set; }
        [FromQuery(Name = "plaka")] public string? Plaka { get; set; }
        /// <summary>Rezervasyon kaynağı.</summary>
        [FromQuery(Name = "kaynak")] public string? Kaynak { get; set; }
        [FromQuery(Name = "sipp")] public string? Sipp { get; set; }
        /// <summary><c>true</c> → KDV dahil GÖSTERİM kolonları (tutarlar değişmez).</summary>
        [FromQuery(Name = "kdvDahil")] public bool? KdvDahil { get; set; }
    }

    /// <summary>P&amp;L toplamları defterden; <c>Toplam*Referans</c> alanları bilgi amaçlıdır, P&amp;L'e EKLENMEZ.</summary>
    public sealed record ProfitabilitySummary(
        decimal ToplamGelir, decimal ToplamGider, decimal ToplamNetKar, bool KdvDahil,
        decimal? ToplamPotansiyelGelir, decimal? ToplamReferansMaliyet, decimal? ToplamHesaplananKdv);

    private static async Task<Ok<ReportResult<ProfitabilitySummary, KarlilikSatirDto>>> Profitability(
        [AsParameters] ReportPeriodQuery q, [AsParameters] ProfitabilityFilter f, [AsParameters] ReportPageQuery page,
        ReportService reports, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var kdv = f.KdvDahil == true ? KdvDurum.KdvDahil : KdvDurum.Kdvsiz;
        var d = await reports.GetKarlilikAsync(p.FromUtc, p.ToUtc, F(f.Sube), F(f.Grup), F(f.Plaka), F(f.Kaynak),
            F(f.Sipp), kdv, ct);
        var mask = await CustomerMask.LoadAsync(dbf, null, ct);
        var rows = d.Satirlar.Select(r => r.CariAd is null ? r : r with { CariAd = mask.Name(r.CariAd) }).ToList();
        var ozet = new ProfitabilitySummary(d.ToplamGelir, d.ToplamGider, d.ToplamNetKar, kdv == KdvDurum.KdvDahil,
            d.ToplamPotansiyelGelir, d.ToplamReferansMaliyet, d.ToplamHesaplananKdv);
        var export = ReportExport.Links(http, user, "karlilik",
        [
            .. ReportExport.Period(p), ("sube", f.Sube), ("grup", f.Grup), ("plaka", f.Plaka), ("kaynak", f.Kaynak),
            ("sipp", f.Sipp), ("kdv", f.KdvDahil == true ? "dahil" : null),
        ]);
        return TypedResults.Ok(new ReportResult<ProfitabilitySummary, KarlilikSatirDto>(p.ToDto(), ozet,
            page.Apply(rows, ProfitabilityMap), export));
    }

    private static readonly SiralamaHaritasi<KarlilikSatirDto> ProfitabilityMap = SiralamaHaritasi<KarlilikSatirDto>
        .Olustur(r => r.Plaka).Alan("plaka", r => r.Plaka).Alan("gelir", r => r.Gelir).Alan("gider", r => r.Gider)
        .Alan("netKar", r => r.NetKar).Alan("sube", r => r.Sube).Alan("grup", r => r.Grup)
        .Alan("dolulukYuzde", r => r.DolulukYuzde);

    private static readonly string[] Dimensions = ["grup", "sube", "segment", "otopark", "sipp"];

    /// <summary>Kârlılığın bir boyuta göre toplamı. <c>boyut</c>: grup | sube | segment | otopark | sipp.</summary>
    private static async Task<Ok<ReportSummaryResult<KarlilikOzetDto>>> ProfitabilityByDimension(
        [AsParameters] ReportPeriodQuery q, string? boyut, ReportService reports, ICurrentUser user, HttpContext http,
        CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var b = F(boyut)?.ToLowerInvariant() ?? "grup";
        if (!Dimensions.Contains(b))
            throw new ValidationException($"Geçersiz boyut. İzin verilenler: {string.Join(", ", Dimensions)}.", "boyut");
        var d = await reports.GetKarlilikOzetAsync(b, p.FromUtc, p.ToUtc, ct);
        return TypedResults.Ok(new ReportSummaryResult<KarlilikOzetDto>(p.ToDto(), d,
            ReportExport.Links(http, user, "karlilik-" + b, ReportExport.Period(p), pdf: true)));
    }

    // ------------------------------------------------------------------ ek hizmet

    private static async Task<Ok<ReportSummaryResult<EkHizmetRaporDto>>> AddOnSales(
        [AsParameters] ReportPeriodQuery q, ReportService reports, ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var d = await reports.GetEkHizmetRaporuAsync(p.FromUtc, p.ToUtc, ct);
        return TypedResults.Ok(new ReportSummaryResult<EkHizmetRaporDto>(p.ToDto(), d,
            ReportExport.Links(http, user, "ek-hizmet", ReportExport.Period(p))));
    }

    /// <summary>Araç × hizmet pivotu (yalnız tarih penceresi; genel toplam = özetin ToplamBrut'u). Satır = filo aracı.</summary>
    private static async Task<Ok<ReportSummaryResult<EkHizmetAracPivotDto>>> AddOnVehiclePivot(
        [AsParameters] ReportPeriodQuery q, ReportService reports, ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var d = await reports.GetEkHizmetAracPivotAsync(p.FromUtc, p.ToUtc, ct);
        return TypedResults.Ok(new ReportSummaryResult<EkHizmetAracPivotDto>(p.ToDto(), d,
            ReportExport.Links(http, user, "ek-hizmet-arac", ReportExport.Period(p))));
    }

    public sealed record AddOnDetailSummary(int Adet, decimal Net, decimal Kdv, decimal Brut);

    /// <summary>Satır bazlı ek hizmet (servis tavanı 2000). <c>sistemGizle=true</c> → SYS-* ücret kalemleri gizli.</summary>
    private static async Task<Ok<ReportResult<AddOnDetailSummary, EkHizmetDetayRow>>> AddOnDetail(
        [AsParameters] ReportPeriodQuery q, string? ara, Guid? personelId, string? ofis, string? kaynak, bool? sistemGizle,
        [AsParameters] ReportPageQuery page, ReportService reports, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var satirlar = await reports.GetEkHizmetDetayAsync(new EkHizmetDetayFilter
        {
            Bas = p.FromUtc, Bit = p.ToUtc, Ara = F(ara), PersonelId = personelId, Ofis = F(ofis),
            RezKaynagi = F(kaynak), SistemKalemleriniGizle = sistemGizle == true,
        }, ct);
        var mask = await CustomerMask.LoadAsync(dbf, null, ct);
        var rows = satirlar.Select(r => r with { MusteriAd = mask.Name(r.MusteriAd) }).ToList();
        var ozet = new AddOnDetailSummary(rows.Count, rows.Sum(r => r.Net), rows.Sum(r => r.Kdv), rows.Sum(r => r.Brut));
        return TypedResults.Ok(new ReportResult<AddOnDetailSummary, EkHizmetDetayRow>(p.ToDto(), ozet,
            page.Apply(rows, AddOnDetailMap), null));
    }

    private static readonly SiralamaHaritasi<EkHizmetDetayRow> AddOnDetailMap = SiralamaHaritasi<EkHizmetDetayRow>
        .Olustur(r => r.AddOnId).Alan("eklenmeTarihi", r => r.EklenmeTarihi).Alan("sozlesmeNo", r => r.SozlesmeNo)
        .Alan("ad", r => r.Ad).Alan("plaka", r => r.Plaka).Alan("brut", r => r.Brut);

    // ------------------------------------------------------------------ günlük faaliyet

    /// <summary>Bir günün sayaçları (varsayılan bugün). Servis günü takvim günü olarak alır → UTC çıpası.</summary>
    private static async Task<Ok<ReportSummaryResult<GunlukFaaliyetDto>>> DailyActivity(
        DateOnly? gun, string? sube, ReportService reports, ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var g = ReportPeriod.ValidateDay(gun, "gun") ?? ReportPeriod.Today;
        var d = await reports.GetGunlukFaaliyetAsync(ReportPeriod.Anchor(g), F(sube), ct);
        return TypedResults.Ok(new ReportSummaryResult<GunlukFaaliyetDto>(new ReportPeriodDto(g, g), d,
            ReportExport.Links(http, user, "gunluk", [("gun", ReportExport.Day(g))])));
    }
}
