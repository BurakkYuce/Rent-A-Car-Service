using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Pricing;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Rapor;

/// <summary>Filo raporları (ViewReports): araç karnesi, filo analiz, filo durumu, doluluk.</summary>
public static partial class ReportApi
{
    private static void MapFleet(RouteGroupBuilder vr)
    {
        vr.MapGet("/arac-karne/{id:guid}", VehicleScorecard);
        vr.MapGet("/filo-analiz", FleetAnalysis);
        vr.MapGet("/filo", FleetStatus);
        vr.MapGet("/doluluk", Occupancy);
        MapFleetDetail(vr);
    }

    // ------------------------------------------------------------------ araç karnesi

    public sealed record ScorecardHeader(
        Guid VehicleId, string Plaka, string? Marka, string? Tip, string? Grup, string? Segment, string? Sube,
        string? AracSahibi, string Durum, int Km, decimal? AlimBedeli, DateTimeOffset? AlimTarihi, decimal? IkinciElDeger,
        DateTimeOffset? FiloGirisTarih, DateTimeOffset? FiloCikisTarih, DateTimeOffset? SonBakimTarih, int? SonBakimKm);

    public sealed record ScorecardCostItem(string Ad, string Periyot, decimal Birim, decimal DonemTutar);

    public sealed record ScorecardCostModel(
        decimal ResidualDeger, decimal NetAmortisman, decimal FinansmanFaiz, decimal FinansmanVergi, decimal Damga,
        decimal ToplamGider, decimal ToplamMaliyet, decimal BasaBasAylik, decimal Kar, decimal TeklifNet,
        decimal TeklifAylikNet, decimal TeklifKdvli, IReadOnlyList<ScorecardCostItem> Kalemler);

    public sealed record ScorecardDue(string Tur, DateTimeOffset Bitis, int KalanGun, string Kova);

    /// <summary>Araç karnesi (Blazor <c>AracKarne</c>): P&amp;L defterden (dönem), KPI ömür boyu, vadeler + bakım km.</summary>
    public sealed record VehicleScorecardReport(
        ScorecardHeader Baslik, decimal ToplamGelir, decimal ToplamGider, decimal ToplamNetKar,
        IReadOnlyList<AracYilPnlRow> YillikPnl, IReadOnlyList<AracKirilimRow> GelirKaynak,
        IReadOnlyList<AracKirilimRow> GiderKategori, IReadOnlyList<AracOlayRow> Olaylar, AracKpiDto Kpi,
        ScorecardCostModel? MaliyetModel, TutSatSinyalDto TutSat, decimal? BasaBasGunluk, KalintiProjeksiyonDto? Kalinti,
        int? DonemKm, decimal? DonemKmMaliyet, IReadOnlyList<ScorecardDue> Vadeler, PeriyodikServisRow? BakimKm);

    private static async Task<Results<Ok<ReportSummaryResult<VehicleScorecardReport>>, ProblemHttpResult>> VehicleScorecard(
        Guid id, [Microsoft.AspNetCore.Http.AsParameters] ReportPeriodQuery q, ReportService reports, VadeService dues,
        ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var d = await reports.GetAracKarneAsync(id, p.FromUtc, p.ToUtc, ct);
        if (d is null) return F5Ortak.Bulunamadi("Araç bulunamadı.");
        var h = d.Header;
        var vadeler = (await dues.GetForVehicleAsync(id, ct: ct))
            .Select(v => new ScorecardDue(v.Tur, v.Bitis, v.KalanGun, v.Bucket.ToString())).ToList();
        var bakim = (await reports.GetPeriyodikServisAsync(ct: ct)).FirstOrDefault(r => r.VehicleId == id);
        var m = d.MaliyetModel;
        var karne = new VehicleScorecardReport(
            new ScorecardHeader(h.VehicleId, h.Plaka, h.Marka, h.Tip, h.Grup, h.Segment, h.Sube, h.AracSahibi,
                h.Durum.ToString(), h.Km, h.AlimBedeli, h.AlimTarihi, h.IkinciElDeger, h.FiloGirisTarih, h.FiloCikisTarih,
                h.SonBakimTarih, h.SonBakimKm),
            d.ToplamGelir, d.ToplamGider, d.ToplamNetKar, d.YillikPnl, d.GelirKaynak, d.GiderKategori, d.Olaylar, d.Kpi,
            m is null ? null : new ScorecardCostModel(m.ResidualDeger, m.NetAmortisman, m.FinansmanFaiz, m.FinansmanVergi,
                m.Damga, m.ToplamGider, m.ToplamMaliyet, m.BasaBasAylik, m.Kar, m.TeklifNet, m.TeklifAylikNet, m.TeklifKdvli,
                m.Kalemler.Select(k => new ScorecardCostItem(k.Ad, k.Periyot.ToString(), k.Birim, k.DonemTutar)).ToList()),
            d.TutSat, d.BasaBasGunluk, d.Kalinti, d.DonemKm, d.DonemKmMaliyet, vadeler, bakim);
        return TypedResults.Ok(new ReportSummaryResult<VehicleScorecardReport>(p.ToDto(), karne,
            ReportExport.Links(http, user, "arac-karne", [("vehicleId", id.ToString()), .. ReportExport.Period(p)])));
    }

    // ------------------------------------------------------------------ filo analiz

    /// <summary>Filo toplamları (dönem P&amp;L defterle mutabık: Σ satır + atanmamış) + yaş kohortu + havuz KPI.</summary>
    public sealed record FleetAnalysisSummary(
        decimal ToplamGelir, decimal ToplamGider, decimal ToplamNetKar, decimal AtanmamisGelir, decimal AtanmamisGider,
        IReadOnlyList<FiloKohortRow> YasKohortu, FiloHavuzKpiDto? HavuzKpi, TutSatAdayOzetDto? TutSatAday,
        IReadOnlyDictionary<Guid, int> VadeUyariSayilari);

    private static readonly string[] FleetOrders = ["net", "zarar", "doluluk", "roi"];

    /// <summary>
    /// Filo analiz panosu. <c>siralama</c> servis sırasıdır (net | zarar | doluluk | roi) — satırlar o sırayla
    /// sayfalanır; <c>sirala</c> ile ayrıca beyaz liste sıralama. Vade uyarı sayıları yalnız sayfadaki araçlar için.
    /// </summary>
    private static async Task<Ok<ReportResult<FleetAnalysisSummary, FiloAnalizRow>>> FleetAnalysis(
        [Microsoft.AspNetCore.Http.AsParameters] ReportPeriodQuery q, string? siralama,
        [Microsoft.AspNetCore.Http.AsParameters] ReportPageQuery page, ReportService reports, VadeService dues,
        ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var sira = F(siralama)?.ToLowerInvariant();
        if (sira is not null && !FleetOrders.Contains(sira))
            throw new Application.Common.ValidationException(
                $"Geçersiz siralama. İzin verilenler: {string.Join(", ", FleetOrders)}.", "siralama");
        var d = await reports.GetFiloAnalizAsync(p.FromUtc, p.ToUtc, sira, ct);
        var sayfa = page.Apply(d.Satirlar, FleetMap);
        var uyarilar = await dues.GetWarningCountsByVehicleAsync(ct: ct);
        var sayfadaki = sayfa.Kayitlar.Select(r => r.VehicleId).ToHashSet();
        var ozet = new FleetAnalysisSummary(d.ToplamGelir, d.ToplamGider, d.ToplamNetKar, d.AtanmamisGelir,
            d.AtanmamisGider, d.YasKohortu, d.HavuzKpi, d.TutSatAday,
            uyarilar.Where(u => sayfadaki.Contains(u.Key)).ToDictionary(u => u.Key, u => u.Value));
        return TypedResults.Ok(new ReportResult<FleetAnalysisSummary, FiloAnalizRow>(p.ToDto(), ozet, sayfa,
            ReportExport.Links(http, user, "filo-analiz", [.. ReportExport.Period(p), ("siralama", sira)])));
    }

    private static readonly Application.Common.SiralamaHaritasi<FiloAnalizRow> FleetMap =
        Application.Common.SiralamaHaritasi<FiloAnalizRow>
            .Olustur(r => r.VehicleId).Alan("plaka", r => r.Plaka).Alan("gelir", r => r.Gelir).Alan("gider", r => r.Gider)
            .Alan("netKar", r => r.NetKar).Alan("dolulukYuzde", r => r.DolulukYuzde).Alan("roiYuzde", r => r.RoiYuzde)
            .Alan("yasAy", r => r.YasAy);

    // ------------------------------------------------------------------ filo durumu + doluluk

    public sealed record FleetStatusReport(FleetUtilizationDto Durum, FiloSubeDto Subeler);

    private static async Task<Ok<ReportSummaryResult<FleetStatusReport>>> FleetStatus(
        ReportService reports, ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var d = await reports.GetFleetUtilizationAsync(ct);
        var s = await reports.GetFleetUtilizationBySubeAsync(ct: ct);
        return TypedResults.Ok(new ReportSummaryResult<FleetStatusReport>(new ReportPeriodDto(null, null),
            new FleetStatusReport(d, s), ReportExport.Links(http, user, "filo", [], pdf: true)));
    }

    public sealed record OccupancyDaily(
        IReadOnlyList<DolulukGunRow> Satirlar, string Boyut, string PaydaAciklama, int DonemGun, int ToplamKiraGun,
        int ToplamRezGun);

    public sealed record OccupancyReport(DolulukDto Ozet, OccupancyDaily Gunluk);

    /// <summary>Dönem doluluğu + gün kırılımı (<c>boyut</c>: Yok | Sube | AracGrubu | RezervasyonKaynagi). Varsayılan
    /// dönem: ayın başı → bugün. Gün tavanı 366 (servis de kırpar).</summary>
    private static async Task<Ok<ReportSummaryResult<OccupancyReport>>> Occupancy(
        [Microsoft.AspNetCore.Http.AsParameters] ReportPeriodQuery q, string? boyut, ReportService reports,
        ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var today = ReportPeriod.Today;
        var p = ReportPeriod.Validate(q.Bas ?? new DateOnly(today.Year, today.Month, 1), q.Bit ?? today, maxDays: 366);
        var b = F5Ortak.EnumAdi<DolulukBoyut>(boyut, "boyut") ?? DolulukBoyut.Yok;
        var from = ReportPeriod.Anchor(p.Bas!.Value);
        var to = ReportPeriod.Anchor(p.Bit!.Value);
        var ozet = await reports.GetDolulukAsync(from, to, ct);
        var g = await reports.GetDolulukGunlukAsync(from, to, b, ct);
        return TypedResults.Ok(new ReportSummaryResult<OccupancyReport>(p.ToDto(), new OccupancyReport(ozet,
                new OccupancyDaily(g.Satirlar, g.Boyut.ToString(), g.PaydaAciklama, g.DonemGun, g.ToplamKiraGun, g.ToplamRezGun)),
            ReportExport.Links(http, user, "doluluk", ReportExport.Period(p))));
    }
}
