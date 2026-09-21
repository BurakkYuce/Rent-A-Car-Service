using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Web.Api;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// RentACar.Web'i (gerçek Program.cs boru hattı) bellek-içi host eder — F1.2 <c>/api/ui</c> testleri için.
/// SpaWebFactory (F1.5) deseni: Development ortamı, ayrı test DB'si, arka plan işleri kapalı.
/// Giriş hız sınırı varsayılan olarak yükseltilir (testler dakikada 10'dan çok giriş yapar); 429 testi kendi
/// dar limitli host'unu kurar. <paramref name="testUclari"/>: hata sözleşmesini GERÇEK grup filtrelerinden
/// geçirmek için test-only uçlar (<see cref="TestUiUclari"/>) eklenir.
/// </summary>
public sealed class WebFactory(PostgresFixture pg, string logYolu, int girisLimiti = 10_000, bool testUclari = true)
    : WebApplicationFactory<RentACar.Web.Common.DogrulamaHatasiMiddleware>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", pg.AppConnectionString);
        builder.UseSetting("ConnectionStrings:Migrator", pg.OwnerConnectionString);
        builder.UseSetting("Logging:FilePath", logYolu);
        builder.UseSetting("RateLimit:LoginPermit", girisLimiti.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureTestServices(s =>
        {
            var isler = s.Where(d => d.ServiceType == typeof(IHostedService)
                                     && d.ImplementationType?.Namespace?.StartsWith("RentACar", StringComparison.Ordinal) == true)
                         .ToList();
            foreach (var d in isler) s.Remove(d);
            if (testUclari) s.AddSingleton<IUiApiUcKaydi, TestUiUclari>();
        });
    }

    public HttpClient Istemci() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}

/// <summary>
/// Test-only <c>/api/ui/v1/test/*</c> uçları: servis istisnalarını (YetkiYok, Validation+Alan, Duplicate*,
/// Mukerrer, beklenmeyen) gerçek grup filtreleri (hata → CSRF → pilot) üzerinden fırlatır. OpenAPI'ye girmez.
/// </summary>
public sealed class TestUiUclari : IUiApiUcKaydi
{
    public const string GizliAyrinti = "GIZLI-IC-AYRINTI-9C1";

    public void Esle(RouteGroupBuilder v1)
    {
        var t = v1.MapGroup("/test").RequirePermission(Permission.OperationsWrite).ExcludeFromDescription();
        t.MapGet("/tamam", () => TypedResults.Ok(new { tamam = true }));
        t.MapPost("/yaz", () => TypedResults.Ok(new { yazildi = true }));
        t.MapGet("/yetki-yok", NoContent () => throw new YetkiYokException("Bu kayıt şube kapsamınız dışında."));
        t.MapPost("/dogrulama", NoContent () => throw new ValidationException("Plaka zorunludur.", "Plaka"));
        t.MapPost("/cakisma", NoContent () => throw new DuplicatePlakaException("34 ABC 123"));
        t.MapPost("/mukerrer", NoContent () => throw new MukerrerIslemException("Bu işlem zaten kaydedildi."));
        t.MapGet("/patla", NoContent () => throw new InvalidOperationException(GizliAyrinti));
        t.MapGet("/finans", () => TypedResults.Ok(new { finans = true })).RequirePermission(Permission.FinanceWrite);
    }
}

/// <summary>
/// Tek Web host'u + ayrı test DB'si. Web açılışı migration + seed yapar (yucerent/demo + kullanıcıları).
/// Tohum: yucerent PİLOT (YeniArayuzPilot=true), demo pilot DEĞİL. Paralellik kapalı: Web açılışı statik
/// <c>FormSecurity.EnforceAntiforgery</c>'yi yazar (FormSecurityTests ile yarışmasın).
/// </summary>
public sealed class WebFixture : IAsyncLifetime
{
    private readonly PostgresFixture _pg = new();
    private readonly string _gecici = Path.Combine(Path.GetTempPath(), "racar-web-" + Guid.NewGuid().ToString("N"));
    private WebFactory? _darLimitli;

    public WebFactory Web { get; private set; } = default!;
    public PostgresFixture Pg => _pg;
    public Guid YucerentId { get; private set; }
    public Guid DemoId { get; private set; }

    /// <summary>Giriş limiti 2 olan ikinci host (429 testi) — ilk erişimde kurulur.</summary>
    public WebFactory DarLimitli => _darLimitli ??= new WebFactory(_pg, Path.Combine(_gecici, "log-dar-.log"), girisLimiti: 2);

    public async Task InitializeAsync()
    {
        await _pg.InitializeAsync();
        Directory.CreateDirectory(_gecici);
        Web = new WebFactory(_pg, Path.Combine(_gecici, "log-web-.log"));
        _ = Web.Server; // host'u başlat → migration + seed

        YucerentId = await TenantIdAsync("yucerent");
        DemoId = await TenantIdAsync("demo");
        await PilotYapAsync(YucerentId, true);
    }

    public async Task<Guid> TenantIdAsync(string kod)
    {
        await using var c = new NpgsqlConnection(_pg.OwnerConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT \"Id\" FROM \"Tenants\" WHERE \"Code\" = @k", c);
        cmd.Parameters.AddWithValue("k", kod);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Pilot bayrağı gerçek repository'den (RLS'li racar_app) yazılır — elle SQL değil.</summary>
    public async Task PilotYapAsync(Guid tenantId, bool pilot)
    {
        using var host = new TestHost(_pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId, role: UserRole.Admin);
        await scope.ServiceProvider.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(s => s.YeniArayuzPilot = pilot);
    }

    public async Task DisposeAsync()
    {
        if (_darLimitli is not null) await _darLimitli.DisposeAsync();
        await Web.DisposeAsync();
        await _pg.DisposeAsync();
        try { Directory.Delete(_gecici, recursive: true); } catch { /* best-effort */ }
    }
}

[CollectionDefinition("web", DisableParallelization = true)]
public sealed class WebCollection : ICollectionFixture<WebFixture>;
