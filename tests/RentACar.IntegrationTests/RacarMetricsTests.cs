using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.Observability;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Identity;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Platform;

namespace RentACar.IntegrationTests;

/// <summary>
/// İş metrikleri (RacarMetrics, Meter "RentACar"). MeterListener ile ölçümler yakalanır; beklenenler
/// senaryodan. Hem metrik plumbing/etiketleri (birim) hem emit-noktası wiring (LoginService entegrasyon).
/// </summary>
[Collection("postgres")]
public sealed class RacarMetricsTests(PostgresFixture fx)
{
    /// <summary>Meter "RentACar" ölçümlerini toplar (dispose'a kadar).</summary>
    private sealed class MetricRecorder : IDisposable
    {
        public readonly List<(string Name, long Value, Dictionary<string, object?> Tags)> M = [];
        private readonly MeterListener _l = new();
        public MetricRecorder()
        {
            _l.InstrumentPublished = (inst, l) => { if (inst.Meter.Name == "RentACar") l.EnableMeasurementEvents(inst); };
            _l.SetMeasurementEventCallback<long>((inst, val, tags, _) =>
                M.Add((inst.Name, val, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
            _l.Start();
        }
        public long Sum(string name, string? tagKey = null, string? tagVal = null) =>
            M.Where(x => x.Name == name && (tagKey is null || (x.Tags.TryGetValue(tagKey, out var v) && (string?)v == tagVal)))
             .Sum(x => x.Value);
        public void Dispose() => _l.Dispose();
    }

    // ---- 1. Birim: her sayaç doğru instrument + düşük-kardinalite etiketle ölçüm yazar ----
    [Fact]
    public void Sayaclar_dogru_instrument_ve_etiketle_yazar()
    {
        using var r = new MetricRecorder();
        RacarMetrics.LoginSuccess();
        RacarMetrics.LoginFail(); RacarMetrics.LoginFail();
        RacarMetrics.TahsilatOk();
        RacarMetrics.TahsilatFail();
        RacarMetrics.LedgerIdempotentRejected();
        RacarMetrics.RateLimitRejected("login");
        RacarMetrics.JobFailed("tcmb-kur");

        Assert.Equal(1, r.Sum("racar.login.total", "result", "success"));
        Assert.Equal(2, r.Sum("racar.login.total", "result", "fail"));
        Assert.Equal(1, r.Sum("racar.tahsilat.total", "result", "ok"));
        Assert.Equal(1, r.Sum("racar.tahsilat.total", "result", "fail"));
        Assert.Equal(1, r.Sum("racar.ledger.idempotent_reject.total"));
        Assert.Equal(1, r.Sum("racar.ratelimit.reject.total", "policy", "login"));
        Assert.Equal(1, r.Sum("racar.job.fail.total", "job", "tcmb-kur"));
    }

    // ---- 2. Entegrasyon: LoginService yanlış parolada login.fail, doğruda login.success artırır ----
    [Fact]
    public async Task LoginService_basarisiz_fail_basarili_success_sayar()
    {
        var appOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options;
        var factory = new ScopedAppDbContextFactory(appOptions, NullTenantContext.Instance, NullCurrentUser.Instance);
        var statusCache = new TenantStatusCache(new MemoryCache(new MemoryCacheOptions()), factory);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Migrator"] = fx.OwnerConnectionString }).Build();
        var platform = new PlatformAdminService(config, new AspNetPasswordHasher(), statusCache, NullLogger<PlatformAdminService>.Instance);
        var login = new LoginService(factory, new PasswordHasher<User>(), NullLogger<LoginService>.Instance);

        var code = "m" + Guid.NewGuid().ToString("N")[..10];
        await platform.CreateTenantAsync(code, "Metrik Co", "admin", "sifre123", "op");

        using var r = new MetricRecorder();
        Assert.Null(await login.ValidateAsync(code, "admin", "YANLIS"));      // fail
        Assert.Null(await login.ValidateAsync("olmayan", "admin", "x"));      // fail (tenant yok)
        Assert.NotNull(await login.ValidateAsync(code, "admin", "sifre123")); // success

        Assert.Equal(2, r.Sum("racar.login.total", "result", "fail"));
        Assert.Equal(1, r.Sum("racar.login.total", "result", "success"));
    }
}
