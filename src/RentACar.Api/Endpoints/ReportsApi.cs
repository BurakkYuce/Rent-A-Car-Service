using RentACar.Application.Authorization;
using RentACar.Application.Reporting;
using RentACar.Api.Common;
using RentACar.Domain.Enums;

namespace RentACar.Api.Endpoints;

/// <summary>Salt-okunur raporlar (JSON). ViewReports (Admin/Yönetici/Muhasebe); tenant izolasyonu
/// JWT→RLS ile otomatik. ReportService DTO'ları doğrudan döner (entity sızdırmaz).</summary>
public static class ReportsApi
{
    public static IEndpointRouteBuilder MapReportsApi(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/reports").WithTags("Reports").RequirePermission(Permission.ViewReports);

        grp.MapGet("/kasa-banka", async (DateTimeOffset? from, DateTimeOffset? to, ReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetCashBankSummaryAsync(from, to, ct)));

        // FAZ-50: hesapId opsiyonel — boş: türün tümü, Guid.Empty: "hesap belirtilmemiş" (legacy) kova.
        grp.MapGet("/account-ledger", async (ReportService svc, CancellationToken ct,
            LedgerAccountType type = LedgerAccountType.Kasa, DateTimeOffset? from = null, DateTimeOffset? to = null,
            Guid? hesapId = null) =>
            Results.Ok(await svc.GetAccountLedgerAsync(type, from, to, hesapId, ct)));

        // FAZ-50: hesap-bazlı kasa/banka özeti (her FinancialAccount ayrı satır + legacy kova).
        grp.MapGet("/kasa-banka/hesaplar", async (DateTimeOffset? from, DateTimeOffset? to, ReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAccountBasedSummaryAsync(from, to, ct)));

        grp.MapGet("/gelir-gider", async (DateTimeOffset? from, DateTimeOffset? to, ReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetRevenueExpenseAsync(from, to, ct)));

        // FAZ-62: filtre parametreleri opsiyonel — hiçbiri verilmezse davranış eskisiyle AYNI.
        // `min` string alınır: boş "?min=" ile gelen istek decimal? bağlamasında 400 verirdi.
        grp.MapGet("/cari-bakiye", async (
            ReportService svc, CancellationToken ct,
            string? ara = null, string? ozelKod = null, string? sinif = null, string? doviz = null,
            string? tip = null, string? bakiye = null, string? min = null) =>
            Results.Ok(await svc.GetAccountBalancesAsync(new CariBakiyeFilter
            {
                Ara = ara, OzelKod = ozelKod, Sinif = sinif, Doviz = doviz,
                Kurumsal = tip switch { "kurumsal" => true, "bireysel" => false, _ => (bool?)null },
                BakiyeTuru = bakiye,
                MinTutar = decimal.TryParse(min, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : null
            }, ct)));

        grp.MapGet("/aging", async (DateTimeOffset? asOf, ReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAgingAsync(asOf ?? DateTimeOffset.UtcNow, ct)));

        grp.MapGet("/filo", async (ReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetFleetUtilizationAsync(ct)));

        grp.MapGet("/servis-maliyet", async (DateTimeOffset? from, DateTimeOffset? to, ReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetServiceCostSummaryAsync(from, to, ct)));

        // FAZ-79: kaynak/sipp filtreleri + kdvDurum (salt gösterim) eklendi; mevcut sorgu-dizesi çağrıları
        // (from/to/sube/grup/plaka) aynen çalışır ve AYNI Gelir/Gider/NetKar'ı döndürür.
        grp.MapGet("/karlilik", async (ReportService svc, CancellationToken ct,
            DateTimeOffset? from = null, DateTimeOffset? to = null, string? sube = null, string? grup = null,
            string? plaka = null, string? kaynak = null, string? sipp = null, bool kdvDahil = false) =>
            Results.Ok(await svc.GetProfitabilityAsync(from, to, sube, grup, plaka, kaynak, sipp,
                kdvDahil ? VatStatus.KdvDahil : VatStatus.Kdvsiz, ct)));

        return app;
    }
}
