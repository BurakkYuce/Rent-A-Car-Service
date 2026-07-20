using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace RentACar.Web.Observability;

/// <summary>
/// OpenTelemetry metrik + trace kurulumu (host-yerel — ASP.NET Core tipleri Application'a giremez).
/// Auto: ASP.NET Core (RED), HttpClient (outbound + W3C traceparent), Npgsql (DB span/metrik), Runtime (GC).
/// İş metrikleri Meter "RentACar" (Application.Observability.RacarMetrics).
/// TAMAMEN config-gated: OTEL_EXPORTER_OTLP_ENDPOINT set DEĞİLSE hiç OTel kurulmaz → instrumentation da
/// yok, exporter da yok → backend'siz kurulumda TAM SIFIR telemetri maliyeti (per-istek Activity üretilmez).
/// NOT: bu kapı iş sayaçlarını (RacarMetrics BCL Meter.Add — dinleyicisizken ucuz no-op) ve OpsWatchdog'u
/// (kendi MeterListener'ıyla doğrudan Meter'a bağlı, OTel'den bağımsız) ETKİLEMEZ.
/// KARDİNALİTE: tenant/user metrikte YOK (yalnız log/trace).
/// </summary>
public static class OpenTelemetrySetup
{
    public static IServiceCollection AddRacarObservability(
        this IServiceCollection services, IConfiguration config, string serviceName)
    {
        // Backend (OTLP endpoint) yoksa OTel'i HİÇ kurma → sıfır maliyet. Endpoint gelince tam kurulur.
        if (string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"])) return services;

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: "1.0.0"))
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation()
                 .AddRuntimeInstrumentation()
                 .AddMeter("RentACar")   // iş metrikleri (login/tahsilat/ledger/ratelimit/job)
                 .AddMeter("Npgsql")      // DB havuz/komut metrikleri
                 .AddOtlpExporter();
            })
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !IsNoise(ctx.Request.Path))
                 .AddHttpClientInstrumentation()
                 .AddSource("Npgsql")     // Npgsql ActivitySource
                 .AddOtlpExporter();
            });
        return services;
    }

    // Blazor circuit websocket'i, framework/statik ve health gürültüsü trace'e girmesin.
    private static bool IsNoise(PathString path)
    {
        var s = path.Value ?? "";
        return s.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || Path.HasExtension(s);
    }
}
