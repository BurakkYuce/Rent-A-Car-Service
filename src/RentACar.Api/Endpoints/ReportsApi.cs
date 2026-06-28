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
            Results.Ok(await svc.GetKasaBankaSummaryAsync(from, to, ct)));

        grp.MapGet("/account-ledger", async (ReportService svc, CancellationToken ct,
            LedgerAccountType type = LedgerAccountType.Kasa, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
            Results.Ok(await svc.GetAccountLedgerAsync(type, from, to, ct)));

        grp.MapGet("/gelir-gider", async (DateTimeOffset? from, DateTimeOffset? to, ReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetGelirGiderAsync(from, to, ct)));

        grp.MapGet("/cari-bakiye", async (ReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetCariBalancesAsync(ct)));

        grp.MapGet("/aging", async (DateTimeOffset? asOf, ReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAgingAsync(asOf ?? DateTimeOffset.UtcNow, ct)));

        grp.MapGet("/filo", async (ReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetFleetUtilizationAsync(ct)));

        grp.MapGet("/servis-maliyet", async (DateTimeOffset? from, DateTimeOffset? to, ReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetServiceCostSummaryAsync(from, to, ct)));

        grp.MapGet("/karlilik", async (ReportService svc, CancellationToken ct,
            DateTimeOffset? from = null, DateTimeOffset? to = null, string? sube = null, string? grup = null, string? plaka = null) =>
            Results.Ok(await svc.GetKarlilikAsync(from, to, sube, grup, plaka, ct)));

        return app;
    }
}
