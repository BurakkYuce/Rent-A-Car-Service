using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Rapor;

/// <summary>Fatura/tahsilat raporları: extre özeti, tahsilat-fatura (+ sözleşme mutabakatı), fatura dönem (+ kira durumu).</summary>
public static partial class ReportApi
{
    private static void MapInvoicePeriod(RouteGroupBuilder vr)
    {
        vr.MapGet("/fatura-donem", InvoicePeriod);
        vr.MapGet("/fatura-donem/kira-durum", RentalInvoiceStatus);
    }

    // ------------------------------------------------------------------ extre özeti

    public sealed record StatementReportSummary(decimal ToplamTl, int GecikmisAdet);

    /// <summary>Fatura seviyesinde extre (BRÜT; iade negatif). Dönem = fatura tarihi.</summary>
    private static async Task<Ok<ReportResult<StatementReportSummary, ExtreOzetiRowDto>>> Statement(
        [AsParameters] ReportPeriodQuery q, Guid? cariId, string? plaka, string? ofis, bool? gecikmis,
        [AsParameters] ReportPageQuery page, ReportService reports, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var simdi = DateTimeOffset.UtcNow;
        var satirlar = await reports.GetExtreOzetiAsync(new ExtreOzetiFilter
        {
            CariId = cariId, Plaka = F(plaka), Ofis = F(ofis), Bas = p.FromUtc, Bit = p.ToUtc,
            YalnizGecikmis = gecikmis == true,
        }, simdi, ct);
        var mask = await CustomerMask.LoadAsync(dbf, satirlar.Select(s => s.CariId), ct);
        var rows = satirlar.Select(s => s with { CariAd = mask.Name(s.CariId, s.CariAd) }).ToList();
        var ozet = new StatementReportSummary(rows.Sum(r => r.IsaretliTutarTl), rows.Count(r => r.KalanGun(simdi) < 0));
        return TypedResults.Ok(new ReportResult<StatementReportSummary, ExtreOzetiRowDto>(p.ToDto(), ozet,
            page.Apply(rows, StatementMap), null));
    }

    private static readonly SiralamaHaritasi<ExtreOzetiRowDto> StatementMap = SiralamaHaritasi<ExtreOzetiRowDto>
        .Olustur(r => r.FaturaId).Alan("tarih", r => r.Tarih).Alan("vadeTarihi", r => r.VadeTarihi)
        .Alan("faturaNo", r => r.FaturaNo).Alan("cariAd", r => r.CariAd).Alan("tutar", r => r.Tutar);

    // ------------------------------------------------------------------ tahsilat-fatura

    private static async Task<Ok<ReportSummaryResult<TahsilatFaturaDto>>> CollectionInvoice(
        [AsParameters] ReportPeriodQuery q, ReportService reports, ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var data = await reports.GetTahsilatFaturaAsync(p.FromUtc, p.ToUtc, ct);
        return TypedResults.Ok(new ReportSummaryResult<TahsilatFaturaDto>(p.ToDto(), data,
            ReportExport.Links(http, user, "tahsilat-fatura", ReportExport.Period(p))));
    }

    /// <summary>Sözleşme satırı mutabakatı (servis DTO'su + enum ADI; türetilmiş kolonlar servis kuralından).</summary>
    public sealed record ReconciliationReportRow(
        Guid RentalId, string SozlesmeNo, string? Plaka, Guid MusteriId, string MusteriAd, DateTimeOffset BasTar,
        string Durum, string Doviz, decimal Matrah, decimal DamgaVergisi, decimal GenelToplam, decimal Tahsilat,
        decimal DefterTahsilat, decimal Faturalanan, decimal MusteriBakiye, decimal Bakiye, decimal FaturaFarki,
        decimal TahsilatAyrimi, bool Tutarsiz);

    public sealed record ReconciliationReportSummary(decimal GenelToplam, decimal Tahsilat, decimal Faturalanan, int TutarsizAdet);

    /// <summary>Dönem = kira başlangıcı (tahsilat-fatura toplamıyla aynı pencere). Servis tavanı 2000 satır.</summary>
    private static async Task<Ok<ReportResult<ReconciliationReportSummary, ReconciliationReportRow>>> CollectionReconciliation(
        [AsParameters] ReportPeriodQuery q, string? ara, string? durum, string? bakiye, bool? tutarsiz,
        [AsParameters] ReportPageQuery page, ReportService reports, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var bd = F(bakiye)?.ToLowerInvariant();
        if (bd is not null and not "acik" and not "kapali")
            throw new ValidationException("Geçersiz bakiye değeri. İzin verilenler: acik, kapali.", "bakiye");
        var satirlar = await reports.GetTahsilatMutabakatAsync(new TahsilatMutabakatFilter
        {
            Ara = F(ara), Durum = F5Ortak.EnumAdi<RentalStatus>(durum, "durum"), BakiyeDurumu = bd,
            YalnizTutarsiz = tutarsiz == true, Bas = p.FromUtc, Bit = p.ToUtc,
        }, ct);
        var mask = await CustomerMask.LoadAsync(dbf, satirlar.Select(s => s.MusteriId), ct);
        var rows = satirlar.Select(s => new ReconciliationReportRow(s.RentalId, s.SozlesmeNo, s.Plaka, s.MusteriId,
            mask.Name(s.MusteriId, s.MusteriAd), s.BasTar, s.Durum.ToString(), s.Doviz, s.Matrah, s.DamgaVergisi,
            s.GenelToplam, s.Tahsilat, s.DefterTahsilat, s.Faturalanan, s.MusteriBakiye, s.Bakiye, s.FaturaFarki,
            s.TahsilatAyrimi, s.Tutarsiz)).ToList();
        var ozet = new ReconciliationReportSummary(rows.Sum(r => r.GenelToplam), rows.Sum(r => r.Tahsilat),
            rows.Sum(r => r.Faturalanan), rows.Count(r => r.Tutarsiz));
        return TypedResults.Ok(new ReportResult<ReconciliationReportSummary, ReconciliationReportRow>(p.ToDto(), ozet,
            page.Apply(rows, ReconciliationMap), null));
    }

    private static readonly SiralamaHaritasi<ReconciliationReportRow> ReconciliationMap = SiralamaHaritasi<ReconciliationReportRow>
        .Olustur(r => r.RentalId).Alan("basTar", r => r.BasTar).Alan("sozlesmeNo", r => r.SozlesmeNo)
        .Alan("musteriAd", r => r.MusteriAd).Alan("genelToplam", r => r.GenelToplam).Alan("bakiye", r => r.Bakiye)
        .Alan("faturaFarki", r => r.FaturaFarki);

    // ------------------------------------------------------------------ fatura dönem

    public sealed record InvoicePeriodSummary(int Adet, decimal ToplamTl);

    /// <summary>Dönemde kesilen faturalar (vade/cari/tutar/durum). Toplam TL = Σ GenelToplam × Kur (iade negatif).</summary>
    private static async Task<Ok<ReportResult<InvoicePeriodSummary, FaturaDonemRow>>> InvoicePeriod(
        [AsParameters] ReportPeriodQuery q, [AsParameters] ReportPageQuery page, ReportService reports,
        ICurrentUser user, IDbContextFactory<AppDbContext> dbf, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var satirlar = await reports.GetFaturaDonemAsync(p.FromUtc, p.ToUtc, ct);
        var mask = await CustomerMask.LoadAsync(dbf, null, ct);
        var rows = satirlar.Select(r => r with { Cari = mask.Name(r.Cari) }).ToList();
        var ozet = new InvoicePeriodSummary(rows.Count, rows.Sum(r => (r.IadeMi ? -r.GenelToplam : r.GenelToplam) * r.Kur));
        return TypedResults.Ok(new ReportResult<InvoicePeriodSummary, FaturaDonemRow>(p.ToDto(), ozet,
            page.Apply(rows, InvoicePeriodMap), ReportExport.Links(http, user, "fatura-donem", ReportExport.Period(p))));
    }

    private static readonly SiralamaHaritasi<FaturaDonemRow> InvoicePeriodMap = SiralamaHaritasi<FaturaDonemRow>
        .Olustur(r => r.InvoiceId).Alan("tarih", r => r.Tarih).Alan("vadeTarihi", r => r.VadeTarihi)
        .Alan("no", r => r.No).Alan("cari", r => r.Cari).Alan("genelToplam", r => r.GenelToplam);

    public sealed record RentalInvoiceSummary(int Adet, int FaturalanmamisAdet, decimal FaturalananTutar);

    /// <summary>Dönemle kesişen kiraların faturalanma durumu. <c>faturaDurum</c>: <c>yok</c> | <c>var</c> | boş.</summary>
    private static async Task<Ok<ReportResult<RentalInvoiceSummary, KiraFaturaDurumRow>>> RentalInvoiceStatus(
        [AsParameters] ReportPeriodQuery q, string? ara, string? faturaDurum, Guid? subeId,
        [AsParameters] ReportPageQuery page, ReportService reports, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        bool? faturalanan = F(faturaDurum)?.ToLowerInvariant() switch
        {
            null => null, "yok" => false, "var" => true,
            _ => throw new ValidationException("Geçersiz faturaDurum değeri. İzin verilenler: yok, var.", "faturaDurum"),
        };
        var satirlar = await reports.GetKiraFaturaDurumAsync(p.FromUtc, p.ToUtc,
            new KiraFaturaDurumFilter { Q = F(ara), Faturalanan = faturalanan, SubeId = subeId }, ct);
        var mask = await CustomerMask.LoadAsync(dbf, null, ct);
        var rows = satirlar.Select(r => r with { Cari = mask.Name(r.Cari) }).ToList();
        var ozet = new RentalInvoiceSummary(rows.Count, rows.Count(r => !r.Faturalanan), rows.Sum(r => r.FaturalananTutar));
        return TypedResults.Ok(new ReportResult<RentalInvoiceSummary, KiraFaturaDurumRow>(p.ToDto(), ozet,
            page.Apply(rows, RentalInvoiceMap),
            ReportExport.Links(http, user, "kira-fatura-durum",
                [.. ReportExport.Period(p), ("q", ara), ("faturaDurum", faturaDurum), ("sube", subeId?.ToString())])));
    }

    private static readonly SiralamaHaritasi<KiraFaturaDurumRow> RentalInvoiceMap = SiralamaHaritasi<KiraFaturaDurumRow>
        .Olustur(r => r.RentalId).Alan("basTar", r => r.BasTar).Alan("sozlesmeNo", r => r.SozlesmeNo)
        .Alan("cari", r => r.Cari).Alan("plaka", r => r.Plaka).Alan("faturalananTutar", r => r.FaturalananTutar);
}
