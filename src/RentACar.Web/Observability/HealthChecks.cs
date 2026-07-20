using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Observability;

/// <summary>Sağlık yanıt yazıcı — MEVCUT sözleşmeyi korur: 200 <c>{"status":"healthy"}</c> / 503
/// <c>{"status":"unhealthy"}</c> (uptime monitörü/testler bu JSON'a bağlı; MapHealthChecks'in düz-metin
/// varsayılanı sözleşmeyi bozardı).</summary>
public static class HealthResponse
{
    public static Task Write(HttpContext ctx, HealthReport report)
    {
        var healthy = report.Status == HealthStatus.Healthy;
        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode = healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        return ctx.Response.WriteAsync($"{{\"status\":\"{(healthy ? "healthy" : "unhealthy")}\"}}");
    }
}

/// <summary>
/// Readiness bağımlılık kontrolleri (tag "ready"). Liveness bunları ÇALIŞTIRMAZ (yalnız süreç ayakta mı).
/// DB: EF CanConnect — MS'in önerdiği yaklaşım (test-sorgusu DEĞİL; DB'yi yormaz). Migrator ve keyring
/// PROD'a özel kritikler: Migrator konsol/migration için; keyring giderse redeploy'da PII çözülemez.
/// </summary>
public sealed class DbConnectHealthCheck(IDbContextFactory<AppDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext ctx, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy("DB (racar_app) bağlanılabilir.")
                : HealthCheckResult.Unhealthy("DB bağlantısı kurulamadı.");
        }
        catch (Exception ex) { return HealthCheckResult.Unhealthy("DB kontrolü başarısız.", ex); }
    }
}

/// <summary>Migrator (racar_owner) bağlantısı açılabiliyor mu — platform konsolu + migration buna bağlı.</summary>
public sealed class MigratorConnectHealthCheck(IConfiguration config) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext ctx, CancellationToken ct = default)
    {
        var conn = config.GetConnectionString("Migrator");
        if (string.IsNullOrWhiteSpace(conn)) return HealthCheckResult.Unhealthy("Migrator bağlantı dizesi yok.");
        try
        {
            await using var c = new NpgsqlConnection(conn);
            await c.OpenAsync(ct);
            return HealthCheckResult.Healthy("Migrator (racar_owner) bağlanılabilir.");
        }
        catch (Exception ex) { return HealthCheckResult.Unhealthy("Migrator bağlantısı başarısız.", ex); }
    }
}

/// <summary>DataProtection keyring dizini okunur/yazılır mı (PII çözümü buna bağlı). RACAR_DP_KEYS unset ise
/// (dev — geçici keyring) sağlıklı sayılır; set ise dizin erişilebilir olmalı.</summary>
public sealed class KeyringHealthCheck(IConfiguration config) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext ctx, CancellationToken _ = default)
    {
        var dir = config["RACAR_DP_KEYS"] ?? Environment.GetEnvironmentVariable("RACAR_DP_KEYS");
        if (string.IsNullOrWhiteSpace(dir))
            return Task.FromResult(HealthCheckResult.Healthy("Keyring geçici (dev) — kalıcı dizin ayarlı değil."));
        try
        {
            if (!Directory.Exists(dir)) return Task.FromResult(HealthCheckResult.Unhealthy($"Keyring dizini yok: {dir}"));
            var probe = Path.Combine(dir, ".health-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return Task.FromResult(HealthCheckResult.Healthy("Keyring dizini okunur/yazılır."));
        }
        catch (Exception ex) { return Task.FromResult(HealthCheckResult.Unhealthy("Keyring dizini erişilemez.", ex)); }
    }
}
