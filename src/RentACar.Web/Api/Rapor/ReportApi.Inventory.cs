using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Personnel;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Rapor;

/// <summary>Sigorta-muayene envanteri, karşılaştırmalı analiz, personel çalışma (vardiya) tablosu.</summary>
public static partial class ReportApi
{
    private static void MapStaffShifts(RouteGroupBuilder ops) => ops.MapGet("/personel-calisma", StaffShifts);

    // ------------------------------------------------------------------ sigorta / muayene

    public sealed record InsuranceInspectionSummary(int Adet, IReadOnlyList<string> AracSahipleri);

    /// <summary>
    /// Araç başına belge/vade envanteri. <c>tur</c>: Hepsi | Trafik | Kasko | Muayene | Mtv | ZIzni | Seyrusefer;
    /// <c>enGec</c>: seçilen türün bitişi bu günden (dahil) önce olanlar. Kapsamlı kullanıcı yalnız şubesinin araçları.
    /// </summary>
    private static async Task<Ok<ReportResult<InsuranceInspectionSummary, SigortaMuayeneRow>>> InsuranceInspection(
        string? tur, DateOnly? enGec, string? sahip, string? plaka, [AsParameters] ReportPageQuery page,
        ReportService reports, ICurrentUser user, CancellationToken ct)
    {
        var t = F5Ortak.EnumAdi<SigortaMuayeneTur>(tur, "tur") ?? SigortaMuayeneTur.Hepsi;
        var gun = ReportPeriod.ValidateDay(enGec, "enGec");
        var kapsam = BranchScope.EffectiveFilter(user);
        bool Gorunur(SigortaMuayeneRow r) => BranchScope.InScope(kapsam, null, r.Sube);
        var rows = (await reports.GetSigortaMuayeneAsync(new SigortaMuayeneFilter
        {
            Tur = t, AracSahibi = F(sahip), Plaka = F(plaka),
            BitisEnGec = gun is { } g ? ReportPeriod.Anchor(g).AddDays(1).AddMicroseconds(-1) : null,
        }, ct)).Where(Gorunur).ToList();
        // Sahip seçenekleri FİLTRESİZ (ama kapsamlı) listeden — seçim sonrası diğerleri kaybolmasın.
        var sahipler = (await reports.GetSigortaMuayeneAsync(ct: ct)).Where(Gorunur).Select(r => r.AracSahibi?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return TypedResults.Ok(new ReportResult<InsuranceInspectionSummary, SigortaMuayeneRow>(
            new ReportPeriodDto(null, gun), new InsuranceInspectionSummary(rows.Count, sahipler),
            page.Apply(rows, InsuranceMap), null));
    }

    private static readonly SiralamaHaritasi<SigortaMuayeneRow> InsuranceMap = SiralamaHaritasi<SigortaMuayeneRow>
        .Olustur(r => r.VehicleId).Alan("plaka", r => r.Plaka).Alan("trafikBitis", r => r.TrafikBitis)
        .Alan("kaskoBitis", r => r.KaskoBitis).Alan("muayeneBitis", r => r.MuayeneBitis).Alan("mtvVade", r => r.MtvVade);

    // ------------------------------------------------------------------ karşılaştırmalı analiz

    public sealed record ComparativeRow(string Kirilim, IReadOnlyDictionary<string, decimal> Aylar, decimal Toplam);

    public sealed record ComparativeReport(
        string Tablo, string VeriTuru, string Kirilim, IReadOnlyList<string> AyAnahtarlari,
        IReadOnlyList<ComparativeRow> Satirlar, IReadOnlyList<decimal> AyToplamlari, decimal GenelToplam);

    private static readonly string[] Tables = ["Kira", "Rezervasyon"];
    private static readonly string[] Measures = ["Adet", "Gun"];
    private static readonly string[] Breakdowns = ["AracGrubu", "RezKaynagi", "CikisNoktasi"];

    /// <summary>
    /// Ay × kırılım hacim pivotu (ADET/GÜN; tutar yok). Pivot firma geneli sayım olduğundan şube kapsamlı kullanıcıya
    /// 403 (Blazor'da operatör tüm firmayı görüyordu — kapsam açığı kapatıldı). Dönem en fazla 3660 gün (ay sütunu tavanı).
    /// </summary>
    private static async Task<Ok<ReportSummaryResult<ComparativeReport>>> ComparativeAnalysis(
        [AsParameters] ReportPeriodQuery q, string? tablo, string? veri, string? kirilim, string? ofis,
        ReportService reports, ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate(maxDays: 3660);
        static string Pick(string[] options, string? value, string fallback, string field)
            => options.FirstOrDefault(o => string.Equals(o, value?.Trim() ?? fallback, StringComparison.OrdinalIgnoreCase))
               ?? throw new ValidationException($"Geçersiz {field}. İzin verilenler: {string.Join(", ", options)}.", field);
        var f = new KarsilastirmaliAnalizFilter
        {
            Tablo = Pick(Tables, F(tablo), "Kira", "tablo"), VeriTuru = Pick(Measures, F(veri), "Adet", "veri"),
            Kirilim = Pick(Breakdowns, F(kirilim), "AracGrubu", "kirilim"), Ofis = F(ofis), Bas = p.FromUtc, Bit = p.ToUtc,
        };
        var d = await reports.GetKarsilastirmaliAnalizAsync(f, ct);
        var rapor = new ComparativeReport(d.Tablo, d.VeriTuru, d.Kirilim, d.AyAnahtarlari,
            d.Satirlar.Select(s => new ComparativeRow(s.Kirilim, s.Aylar, s.Toplam)).ToList(),
            d.AyAnahtarlari.Select(d.AyToplami).ToList(), d.GenelToplam);
        return TypedResults.Ok(new ReportSummaryResult<ComparativeReport>(p.ToDto(), rapor,
            ReportExport.Links(http, user, "karsilastirmali-analiz",
                [("tablo", f.Tablo), ("veri", f.VeriTuru), ("kirilim", f.Kirilim), ("ofis", ofis), .. ReportExport.Period(p)])));
    }

    // ------------------------------------------------------------------ personel çalışma (vardiya)

    public sealed record ShiftRow(
        Guid Id, Guid PersonelId, string PersonelAd, string? PersonelKadroSube, DateOnly Tarih, TimeOnly BaslangicSaat,
        TimeOnly BitisSaat, int SureDk, string Aralik, string? Sube, string? Aciklama);

    public sealed record ShiftDay(DateOnly Gun, IReadOnlyList<ShiftRow> Vardiyalar);

    public sealed record ShiftMatrixRow(Guid PersonelId, string PersonelAd, IReadOnlyList<ShiftDay> Gunler, int ToplamDk, string ToplamSaatMetni);

    public sealed record ShiftReport(
        DateOnly Bas, DateOnly Bit, bool Kirpildi, IReadOnlyList<DateOnly> Gunler, IReadOnlyList<ShiftMatrixRow> Matris,
        int ToplamVardiya, int ToplamDk, IReadOnlyList<ShiftRow> Liste);

    /// <summary>
    /// Personel × gün vardiya matrisi + düz liste (Blazor <c>PersonelCalismaTablosu</c>). Kapsam ve izin serviste
    /// (ViewReports VEYA OperationsWrite; vardiyanın şubesine göre). Pencere en fazla 92 gün — servis kırpar,
    /// <c>kirpildi</c> bildirir. Vardiya yazma uçları bu PR'da YOK (rapor PR'ı yalnız okur).
    /// </summary>
    private static async Task<Ok<ShiftReport>> StaffShifts(
        DateOnly? bas, DateOnly? bit, Guid? personelId, string? sube, PersonelVardiyaService shifts, CancellationToken ct)
    {
        ReportPeriod.Validate(bas, bit);
        var filtre = new VardiyaFilter { Bas = bas, Bit = bit, PersonelId = personelId, Sube = F(sube) };
        var (b, t) = PersonelVardiyaService.Pencere(filtre);
        var kirpildi = bas is { } fb && bit is { } ft && ft.DayNumber - fb.DayNumber + 1 > PersonelVardiyaService.MaxGun;
        var matris = await shifts.MatrisAsync(filtre, ct);
        var liste = await shifts.ListAsync(filtre, ct);
        var adlar = liste.ToDictionary(x => x.Vardiya.Id, x => (x.PersonelAd, x.PersonelKadroSube));
        ShiftRow Row(Domain.Entities.PersonelVardiya v, string ad, string? kadro) => new(v.Id, v.PersonelId, ad, kadro,
            v.Tarih, v.BaslangicSaat, v.BitisSaat, v.SureDk, VardiyaBicim.Aralik(v), v.Sube, v.Aciklama);
        var satirlar = matris.Satirlar.Select(s => new ShiftMatrixRow(s.PersonelId, s.PersonelAd,
            s.Gunler.OrderBy(k => k.Key).Select(k => new ShiftDay(k.Key, k.Value.Select(v =>
                Row(v, s.PersonelAd, adlar.TryGetValue(v.Id, out var a) ? a.PersonelKadroSube : null)).ToList())).ToList(),
            s.ToplamDk, s.ToplamSaatMetni)).ToList();
        return TypedResults.Ok(new ShiftReport(b, t, kirpildi, matris.Gunler, satirlar, matris.ToplamVardiya, matris.ToplamDk,
            liste.Select(x => Row(x.Vardiya, x.PersonelAd, x.PersonelKadroSube)).ToList()));
    }
}
