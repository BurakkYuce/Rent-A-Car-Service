using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Observability;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace RentACar.IntegrationTests;

/// <summary>
/// Observability PR1: log zenginleştirme (tenant/user/request_id) + readiness health-check'leri.
/// Enrichment gerçek Serilog LogContext akışıyla doğrulanır (yakalayıcı sink); health-check'ler canlı DB'ye
/// karşı. Beklenenler senaryodan.
/// </summary>
[Collection("postgres")]
public sealed class ObservabilityTests(PostgresFixture fx)
{
    // ---- yakalayıcı Serilog sink ----
    private sealed class ListSink(List<LogEvent> sink) : ILogEventSink
    {
        public void Emit(LogEvent le) => sink.Add(le);
    }

    private static ClaimsPrincipal Authed(string tenantId, string user) => new(new ClaimsIdentity(
        [new Claim("tenant_id", tenantId), new Claim(ClaimTypes.Name, user)], "test"));

    private static HttpContext Ctx(ClaimsPrincipal? user)
    {
        var c = new DefaultHttpContext();
        if (user is not null) c.User = user;
        return c;
    }

    // ---- 1. Middleware: authenticated istekte istek-içi loglar tenant/user/req-id taşır ----
    [Fact]
    public async Task Middleware_authed_istek_ici_loglari_zenginlestirir()
    {
        var events = new List<LogEvent>();
        var log = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(new ListSink(events)).CreateLogger();

        var ctx = Ctx(Authed("11111111-1111-1111-1111-111111111111", "umit"));
        await new RequestEnrichment.Middleware().InvokeAsync(ctx, _ =>
        {
            log.Information("iç log"); // middleware LogContext scope'u İÇİNDE
            return Task.CompletedTask;
        });

        var ev = Assert.Single(events);
        Assert.Equal("11111111-1111-1111-1111-111111111111", ev.Properties["tenant_id"].ToString().Trim('"'));
        Assert.Equal("umit", ev.Properties["user"].ToString().Trim('"'));
        Assert.True(ev.Properties.ContainsKey("request_id"));
    }

    // ---- 2. Anonim istek: yalnız request_id (tenant/user yok — sızıntı yok) ----
    [Fact]
    public async Task Middleware_anonim_yalniz_request_id()
    {
        var events = new List<LogEvent>();
        var log = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(new ListSink(events)).CreateLogger();

        await new RequestEnrichment.Middleware().InvokeAsync(Ctx(null), _ => { log.Information("x"); return Task.CompletedTask; });

        var ev = Assert.Single(events);
        Assert.True(ev.Properties.ContainsKey("request_id"));
        Assert.False(ev.Properties.ContainsKey("tenant_id"));
        Assert.False(ev.Properties.ContainsKey("user"));
    }

    // ---- 3. Platform admin: tenant_id claim'i yok → "platform" damgası ----
    [Fact]
    public async Task Middleware_platform_admin_tenant_platform_olur()
    {
        var events = new List<LogEvent>();
        var log = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(new ListSink(events)).CreateLogger();
        var pa = new ClaimsPrincipal(new ClaimsIdentity([new Claim("platform_admin", "true"), new Claim(ClaimTypes.Name, "admin")], "test"));

        await new RequestEnrichment.Middleware().InvokeAsync(Ctx(pa), _ => { log.Information("x"); return Task.CompletedTask; });

        Assert.Equal("platform", Assert.Single(events).Properties["tenant_id"].ToString().Trim('"'));
    }

    // ---- 4. DB readiness health-check: canlı DB'de Healthy ----
    [Fact]
    public async Task Db_health_check_saglikli()
    {
        var factory = new ScopedAppDbContextFactory(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options,
            NullTenantContext.Instance, NullCurrentUser.Instance);
        var check = new DbConnectHealthCheck(factory);
        var res = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, res.Status);
    }

    // ---- 5. Migrator health-check: owner bağlantısıyla Healthy; conn yoksa Unhealthy ----
    [Fact]
    public async Task Migrator_health_check_owner_conn_ile_saglikli_yoksa_degil()
    {
        var okConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Migrator"] = fx.OwnerConnectionString }).Build();
        Assert.Equal(HealthStatus.Healthy, (await new MigratorConnectHealthCheck(okConfig).CheckHealthAsync(new())).Status);

        var noConfig = new ConfigurationBuilder().Build();
        Assert.Equal(HealthStatus.Unhealthy, (await new MigratorConnectHealthCheck(noConfig).CheckHealthAsync(new())).Status);
    }

    // ---- 6. Keyring health-check: unset (dev) Healthy; var-olmayan dizin Unhealthy; geçerli dizin Healthy ----
    [Fact]
    public async Task Keyring_health_check_durumlari()
    {
        var unset = new ConfigurationBuilder().Build();
        Assert.Equal(HealthStatus.Healthy, (await new KeyringHealthCheck(unset).CheckHealthAsync(new())).Status);

        var none = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["RACAR_DP_KEYS"] = "/kesinlikle/olmayan/dizin/xyz" }).Build();
        Assert.Equal(HealthStatus.Unhealthy, (await new KeyringHealthCheck(none).CheckHealthAsync(new())).Status);

        var tmp = Path.Combine(Path.GetTempPath(), "racar-keyring-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var okConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["RACAR_DP_KEYS"] = tmp }).Build();
            Assert.Equal(HealthStatus.Healthy, (await new KeyringHealthCheck(okConfig).CheckHealthAsync(new())).Status);
        }
        finally { Directory.Delete(tmp, true); }
    }
}
