using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace RentACar.Web.Observability;

/// <summary>
/// OpenTelemetry metrik + trace kurulumu (host-yerel — ASP.NET Core tipleri Application'a giremez).
/// Auto: ASP.NET Core (RED), HttpClient (outbound + W3C traceparent), Npgsql (DB span/metrik), Runtime (GC).
/// İş metrikleri Meter "RentACar" (Application.Observability.RacarMetrics).
/// EXPORTER config-gated: yalnız OTEL_EXPORTER_OTLP_ENDPOINT set ise OTLP eklenir → endpoint yoksa
/// exporter hiç kurulmaz (dev/test/CI'de connection-refused gürültüsü ve yük yok).
/// KARDİNALİTE: tenant/user metrikte YOK (yalnız log/trace).
/// </summary>
public static class OpenTelemetrySetup
{
    public static IServiceCollection AddRacarObservability(
        this IServiceCollection services, IConfiguration config, string serviceName)
    {
        var hasOtlp = !string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: "1.0.0"))
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation()
                 .AddRuntimeInstrumentation()
                 .AddMeter("RentACar")   // iş metrikleri (login/tahsilat/ledger/ratelimit/job)
                 .AddMeter("Npgsql");     // DB havuz/komut metrikleri
                if (hasOtlp) m.AddOtlpExporter();
            })
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !IsNoise(ctx.Request.Path))
                 .AddHttpClientInstrumentation()
                 .AddSource("Npgsql");    // Npgsql ActivitySource
                if (hasOtlp) t.AddOtlpExporter();
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
