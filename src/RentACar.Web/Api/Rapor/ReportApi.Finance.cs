using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Api.Rapor;

/// <summary>Finans analiz panosu, virman geçmişi, KDV listesi (oran özeti + belge bazlı geniş görünüm).</summary>
public static partial class ReportApi
{
    // ------------------------------------------------------------------ finans analiz

    public sealed record ReportAgingBuckets(decimal B0_30, decimal B31_60, decimal B61_90, decimal B90Plus);

    /// <summary>
    /// Finans analiz panosu (Blazor <c>FinansAnaliz</c>): 12 ay gelir/gider/net trendi (dönemden bağımsız), dönem
    /// gelir/gider kırılımı, yaşlandırma kova toplamları (tarih = dönem sonu ya da şimdi), fatura-tahsilat mutabakatı,
    /// son 30 gün araç durum serisi (yüzde SPA'da: Dolu ÷ ToplamArac).
    /// </summary>
    public sealed record FinanceDashboard(
        IReadOnlyList<AylikGelirGiderNokta> Trend,
        IReadOnlyList<GelirGiderKalemDto> GelirKirilim,
        IReadOnlyList<GelirGiderKalemDto> GiderKirilim,
        ReportAgingBuckets Yaslandirma,
        TahsilatFaturaDto Mutabakat,
        IReadOnlyList<AracDurumTakipRow> Son30Gun);

    private static async Task<Ok<ReportSummaryResult<FinanceDashboard>>> FinanceAnalysis(
        [AsParameters] ReportPeriodQuery q, ReportService reports, ICurrentUser user, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var trend = await reports.GetMonthlyRevenueExpenseTrendAsync(12, ct: ct);
        var gg = await reports.GetRevenueExpenseAsync(p.FromUtc, p.ToUtc, ct);
        var aging = await reports.GetAgingAsync(p.Bit is { } b ? ReportPeriod.Anchor(b) : DateTimeOffset.UtcNow, ct);
        var tf = await reports.GetCollectionInvoiceAsync(p.FromUtc, p.ToUtc, ct);
        var bugun = ReportPeriod.Anchor(ReportPeriod.Today);
        var takip = await reports.GetVehicleStatusTrackingAsync(new AracDurumTakipFilter(), bugun.AddDays(-29), bugun, ct);
        var kova = new ReportAgingBuckets(aging.Sum(a => a.B0_30), aging.Sum(a => a.B31_60), aging.Sum(a => a.B61_90),
            aging.Sum(a => a.B90Plus));
        return TypedResults.Ok(new ReportSummaryResult<FinanceDashboard>(p.ToDto(),
            new FinanceDashboard(trend, gg.GelirKirilim, gg.GiderKirilim, kova, tf, takip), null));
    }

    // ------------------------------------------------------------------ virman geçmişi

    public sealed record ReportTransferRow(
        Guid Id, DateTimeOffset Tarih, string KaynakTur, Guid? KaynakHesapId, string? KaynakHesapAd,
        string HedefTur, Guid? HedefHesapId, string? HedefHesapAd, decimal Tutar, string Doviz, decimal Kur,
        decimal TutarTl, string? MakbuzNo, string? Sube, string? IslemYapan, string? Aciklama, bool KunyeVar);

    public sealed record ReportCurrencyTotal(string Doviz, decimal Toplam);

    /// <summary>Kasa/banka virman geçmişi (servis tavanı 200 satır). Toplam DÖVİZ KIRILIMLI (farklı döviz toplanmaz).</summary>
    private static async Task<Ok<ReportResult<IReadOnlyList<ReportCurrencyTotal>, ReportTransferRow>>> TransferHistory(
        [AsParameters] ReportPeriodQuery q, Guid? hesapId, string? ara, [AsParameters] ReportPageQuery page,
        CashService cash, FinancialAccountService accounts, ICurrentUser user, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var satirlar = await cash.ListCashTransfersAsync(new KasaVirmanFilter
        {
            Bas = p.FromUtc, Bit = p.ToUtc, HesapId = hesapId, Ara = F(ara),
        }, ct);
        var adlar = (await accounts.ListAsync(ct)).ToDictionary(h => h.Id, h => h.Ad);
        string? Ad(Guid? id) => id is { } i ? adlar.GetValueOrDefault(i, "(silinmiş hesap)") : null;
        var rows = satirlar.Select(s => new ReportTransferRow(s.Id, s.Tarih, s.KaynakTur.ToString(), s.KaynakHesapId,
            Ad(s.KaynakHesapId), s.HedefTur.ToString(), s.HedefHesapId, Ad(s.HedefHesapId), s.Tutar, s.Doviz, s.Kur,
            s.TutarTl, s.MakbuzNo, s.Sube, s.IslemYapan, s.Aciklama, s.KunyeVar)).ToList();
        var toplam = rows.GroupBy(r => r.Doviz).Select(g => new ReportCurrencyTotal(g.Key, g.Sum(x => x.Tutar)))
            .OrderBy(x => x.Doviz, StringComparer.Ordinal).ToList();
        return TypedResults.Ok(new ReportResult<IReadOnlyList<ReportCurrencyTotal>, ReportTransferRow>(
            p.ToDto(), toplam, page.Apply(rows, TransferMap), null));
    }

    private static readonly SortFieldMap<ReportTransferRow> TransferMap = SortFieldMap<ReportTransferRow>
        .Create(r => r.Id).Alan("tarih", r => r.Tarih).Alan("tutar", r => r.Tutar).Alan("tutarTl", r => r.TutarTl)
        .Alan("doviz", r => r.Doviz).Alan("sube", r => r.Sube);

    // ------------------------------------------------------------------ KDV

    private static async Task<Ok<ReportSummaryResult<KdvListesiDto>>> VatList(
        [AsParameters] ReportPeriodQuery q, ReportService reports, ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var data = await reports.GetVatListAsync(p.FromUtc, p.ToUtc, ct);
        return TypedResults.Ok(new ReportSummaryResult<KdvListesiDto>(p.ToDto(), data,
            ReportExport.Links(http, user, "kdv-listesi", ReportExport.Period(p))));
    }

    /// <summary>KDV geniş özeti (belge satırları <see cref="ReportResult{TSummary,TRow}.Satirlar"/>'da).</summary>
    public sealed record VatWideSummary(
        decimal SatisNet, decimal SatisKdv, decimal AlisNet, decimal AlisKdv, decimal NetKdv,
        int SatisBelgeAdet, int AlisBelgeAdet, int AtlananDovizliAlis);

    /// <summary>Belge bazlı KDV (satır = belge). <c>alis=true</c> → gelen e-Fatura (indirilecek KDV) dahil.
    /// Satış satırının cari adı KVKK kuralıyla; alış satırı tedarikçidir (müşteri değil).</summary>
    private static async Task<Ok<ReportResult<VatWideSummary, KdvGenisSatirDto>>> VatWide(
        [AsParameters] ReportPeriodQuery q, bool? alis, [AsParameters] ReportPageQuery page, ReportService reports,
        ICurrentUser user, IDbContextFactory<AppDbContext> dbf, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var d = await reports.GetVatExtendedAsync(p.FromUtc, p.ToUtc, alis == true, ct);
        var mask = await CustomerMask.LoadAsync(dbf, null, ct);
        var rows = d.Satirlar.Select(r => r.AlisMi ? r : r with { Cari = mask.Name(r.Cari) }).ToList();
        var ozet = new VatWideSummary(d.SatisNet, d.SatisKdv, d.AlisNet, d.AlisKdv, d.NetKdv,
            d.SatisBelgeAdet, d.AlisBelgeAdet, d.AtlananDovizliAlis);
        return TypedResults.Ok(new ReportResult<VatWideSummary, KdvGenisSatirDto>(p.ToDto(), ozet,
            page.Apply(rows, VatWideMap),
            ReportExport.Links(http, user, "kdv-genis", [.. ReportExport.Period(p), ("alis", alis == true ? "1" : null)])));
    }

    private static readonly SortFieldMap<KdvGenisSatirDto> VatWideMap = SortFieldMap<KdvGenisSatirDto>
        .Create(r => r.BelgeId).Alan("tarih", r => r.Tarih).Alan("no", r => r.No).Alan("tur", r => r.Tur)
        .Alan("cari", r => r.Cari).Alan("toplamNet", r => r.ToplamNet).Alan("toplamKdv", r => r.ToplamKdv);
}
