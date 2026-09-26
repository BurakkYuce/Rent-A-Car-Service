using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Api.Rapor;

/// <summary>Defter raporları: gelir-gider, kasa/banka defteri, finans analiz, virman geçmişi, KDV listesi.</summary>
public static partial class ReportApi
{
    private static void MapLedger(RouteGroupBuilder vr)
    {
        vr.MapGet("/gelir-gider", IncomeExpense);
        vr.MapGet("/kasa-banka", CashBankLedger);
        vr.MapGet("/finans-analiz", FinanceAnalysis);
        vr.MapGet("/virman-gecmisi", TransferHistory);
        vr.MapGet("/kdv-listesi", VatList);
        vr.MapGet("/kdv-listesi/genis", VatWide);
    }

    // ------------------------------------------------------------------ gelir-gider

    private static async Task<Ok<ReportSummaryResult<GelirGiderDto>>> IncomeExpense(
        [AsParameters] ReportPeriodQuery q, ReportService reports, ICurrentUser user, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var data = await reports.GetRevenueExpenseAsync(p.FromUtc, p.ToUtc, ct);
        return TypedResults.Ok(new ReportSummaryResult<GelirGiderDto>(p.ToDto(), data,
            ReportExport.Links(http, user, "gelir-gider", ReportExport.Period(p))));
    }

    // ------------------------------------------------------------------ kasa / banka defteri

    /// <summary>Kasa/banka defteri süzgeçleri (Blazor <c>KasaBankaDefteri</c> ile aynı adlar).</summary>
    public sealed class CashBankReportFilter
    {
        /// <summary><c>Kasa</c> (varsayılan) | <c>Banka</c>.</summary>
        [FromQuery(Name = "hesap")] public string? Hesap { get; set; }
        /// <summary>Hesap kimliği; <c>00000000-…</c> = yalnız "hesap belirtilmemiş" kayıtlar; boş = tümü.</summary>
        [FromQuery(Name = "hesapId")] public Guid? HesapId { get; set; }
        [FromQuery(Name = "doviz")] public string? Doviz { get; set; }
        /// <summary>İşlem türü (defter SourceType).</summary>
        [FromQuery(Name = "tur")] public string? Tur { get; set; }
        [FromQuery(Name = "sube")] public string? Sube { get; set; }
        /// <summary>Dönem başına devir satırı eklensin mi.</summary>
        [FromQuery(Name = "devir")] public bool? Devir { get; set; }
    }

    public sealed record ReportAccountSummaryRow(string Tur, Guid? HesapId, string? HesapAd, decimal Giris, decimal Cikis, decimal Bakiye);

    public sealed record CashBankReportOptions(IReadOnlyList<string> Dovizler, IReadOnlyList<string> Turler, IReadOnlyList<string> Subeler);

    public sealed record CashBankReportSummary(
        string Hesap, CashboxSummaryDto Toplam, IReadOnlyList<ReportAccountSummaryRow> Hesaplar, CashBankReportOptions Secenekler);

    /// <summary>
    /// Kasa/banka defteri. Satırlar servisin kronolojik sırasındadır (yürüyen bakiye sıraya bağlı — sıralama
    /// parametresi YOK); sayfa yalnız dilimler. Cari adı KVKK kuralıyla.
    /// </summary>
    private static async Task<Ok<ReportResult<CashBankReportSummary, LedgerLineDto>>> CashBankLedger(
        [AsParameters] ReportPeriodQuery q, [AsParameters] CashBankReportFilter f, int? sayfa, int? boyut,
        ReportService reports, FinancialAccountService accounts, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var p = q.Validate();
        var type = string.IsNullOrWhiteSpace(f.Hesap) ? LedgerAccountType.Kasa
            : string.Equals(f.Hesap.Trim(), "Banka", StringComparison.OrdinalIgnoreCase) ? LedgerAccountType.Banka
            : string.Equals(f.Hesap.Trim(), "Kasa", StringComparison.OrdinalIgnoreCase) ? LedgerAccountType.Kasa
            : throw new ValidationException("Geçersiz hesap değeri. İzin verilenler: Kasa, Banka.", "hesap");

        var total = await reports.GetCashBankSummaryAsync(p.FromUtc, p.ToUtc, ct);
        var accountSummary = await reports.GetAccountBasedSummaryAsync(p.FromUtc, p.ToUtc, ct);
        var rows = await reports.GetAccountLedgerAsync(type, p.FromUtc, p.ToUtc, f.HesapId, ct,
            currency: F(f.Doviz), transactionType: F(f.Tur), branch: F(f.Sube), carryForward: f.Devir == true);
        var all = await reports.GetAccountLedgerAsync(type, p.FromUtc, p.ToUtc, ct: ct);
        var names = (await accounts.ListAsync(ct)).ToDictionary(h => h.Id, h => h.Ad);
        var mask = await CustomerMask.LoadAsync(dbf, null, ct);

        var visible = rows.Select(l => l with { CariAd = l.CariAd is null ? null : mask.Name(l.CariAd) }).ToList();
        var option = new CashBankReportOptions(
            Distinct(all.Select(x => x.Doviz), StringComparer.OrdinalIgnoreCase),
            Distinct(all.Select(x => x.SourceType), StringComparer.Ordinal),
            Distinct(all.Select(x => x.Sube?.Trim()), StringComparer.OrdinalIgnoreCase));
        var summary = new CashBankReportSummary(type.ToString(), total,
            accountSummary.Select(h => new ReportAccountSummaryRow(h.Tur.ToString(), h.HesapId,
                h.HesapId is { } id ? names.GetValueOrDefault(id, "(silinmiş hesap)") : null,
                h.Giris, h.Cikis, h.Bakiye)).ToList(), option);

        var request = new ListeIstegi(sayfa ?? 1, boyut ?? 50);
        var records = request.Atla >= visible.Count ? [] : visible.Skip((int)request.Atla).Take(request.Boyut).ToList();
        var export = ReportExport.Links(http, user, "kasa-banka",
        [
            ("hesap", type.ToString()), ("hesapId", f.HesapId?.ToString()), .. ReportExport.Period(p),
            ("doviz", f.Doviz), ("tur", f.Tur), ("sube", f.Sube), ("devir", f.Devir == true ? "true" : null),
        ]);
        return TypedResults.Ok(new ReportResult<CashBankReportSummary, LedgerLineDto>(p.ToDto(), summary,
            new Sayfa<LedgerLineDto>(records, visible.Count, request.Sayfa, request.Boyut), export));
    }

    private static string? F(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static List<string> Distinct(IEnumerable<string?> src, StringComparer cmp)
        => src.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!)
            .Distinct(cmp).OrderBy(x => x, StringComparer.Ordinal).ToList();
}
