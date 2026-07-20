using System.Diagnostics;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RentACar.Api.Identity;
using RentACar.Infrastructure.Persistence;
using Serilog;
using Serilog.Context;

namespace RentACar.Api.Observability;

/// <summary>API OpenTelemetry kurulumu (Web ile aynı desen; config-gated OTLP). İş metrikleri paylaşılan
/// Meter "RentACar" (Application) — login vb. Infra'dan emit edilir, API host'unda toplanır.</summary>
public static class ApiObservabilitySetup
{
    public static IServiceCollection AddRacarObservability(
        this IServiceCollection services, IConfiguration config, string serviceName)
    {
        var hasOtlp = !string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: "1.0.0"))
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation()
                 .AddMeter("RentACar").AddMeter("Npgsql");
                if (hasOtlp) m.AddOtlpExporter();
            })
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !(ctx.Request.Path.Value ?? "").StartsWith("/health", StringComparison.OrdinalIgnoreCase))
                 .AddHttpClientInstrumentation().AddSource("Npgsql");
                if (hasOtlp) t.AddOtlpExporter();
            });
        return services;
    }
}

/// <summary>API istek-kapsamlı log zenginleştirme (Web ile aynı claim değerleri: tenant_id/user).
/// (a) middleware LogContext ile istek-içi tüm logları; (b) Enrich, request-completion olayını.
/// Kardinalite: tenant/user yalnız log — metrik etiketi değil.</summary>
public static class ApiRequestEnrichment
{
    private static string TenantId(ClaimsPrincipal u) => u.FindFirst(ApiClaims.TenantId)?.Value ?? "-";
    private static string User(ClaimsPrincipal u) => u.Identity?.Name ?? "-";
    private static string RequestId(HttpContext ctx) => Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;

    public static void Enrich(IDiagnosticContext diag, HttpContext ctx)
    {
        var u = ctx.User;
        if (u.Identity?.IsAuthenticated == true) { diag.Set("tenant_id", TenantId(u)); diag.Set("user", User(u)); }
        diag.Set("request_id", RequestId(ctx));
    }

    public sealed class Middleware : IMiddleware
    {
        public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
        {
            var u = ctx.User;
            if (u.Identity?.IsAuthenticated != true)
            {
                using (LogContext.PushProperty("request_id", RequestId(ctx))) await next(ctx);
                return;
            }
            using (LogContext.PushProperty("tenant_id", TenantId(u)))
            using (LogContext.PushProperty("user", User(u)))
            using (LogContext.PushProperty("request_id", RequestId(ctx)))
                await next(ctx);
        }
    }
}

/// <summary>Sağlık yanıt yazıcı — mevcut sözleşme: 200 {"status":"healthy"} / 503 {"status":"unhealthy"}.</summary>
public static class ApiHealthResponse
{
    public static Task Write(HttpContext ctx, HealthReport report)
    {
        var healthy = report.Status == HealthStatus.Healthy;
        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode = healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        return ctx.Response.WriteAsync($"{{\"status\":\"{(healthy ? "healthy" : "unhealthy")}\"}}");
    }
}

/// <summary>API readiness: DB (racar_app) CanConnect — MS deseni (test-sorgusu değil).</summary>
public sealed class ApiDbHealthCheck(IDbContextFactory<AppDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext ctx, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy("DB bağlanılabilir.")
                : HealthCheckResult.Unhealthy("DB bağlantısı kurulamadı.");
        }
        catch (Exception ex) { return HealthCheckResult.Unhealthy("DB kontrolü başarısız.", ex); }
    }
}
