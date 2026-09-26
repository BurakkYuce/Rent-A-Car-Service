using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// RentACar.Web'i (gerçek Program.cs boru hattı) bellek-içi host eder — F1.2 <c>/api/ui</c> testleri için.
/// SpaWebFactory (F1.5) deseni: Development ortamı, ayrı test DB'si, arka plan işleri kapalı.
/// Platform operatörü kimliği çalışma anında üretilir (<see cref="WebFixture.Platform"/>).
/// Giriş hız sınırı varsayılan olarak yükseltilir (testler dakikada 10'dan çok giriş yapar); 429 testi kendi
/// dar limitli host'unu kurar. <paramref name="testEndpoints"/>: hata sözleşmesini GERÇEK grup filtrelerinden
/// geçirmek için test-only uçlar (<see cref="TestUiEndpoints"/>) eklenir.
/// </summary>
public sealed class WebFactory(PostgresFixture pg, string logPath, TestKimlik platform, int loginLimit = 10_000, bool testEndpoints = true,
    int clientErrorLimit = 10_000)
    : WebApplicationFactory<RentACar.Web.Common.ValidationErrorMiddleware>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", pg.AppConnectionString);
        builder.UseSetting("ConnectionStrings:Migrator", pg.OwnerConnectionString);
        builder.UseSetting("Logging:FilePath", logPath);
        builder.UseSetting("RateLimit:LoginPermit", loginLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimit:IstemciHataPermit", clientErrorLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimit:ExternalActionPermit", "10000"); // F11.1b: testler tek IP'den koşar
        // Platform operatörü: dev varsayılanı yerine çalışma anında üretilen kimlik (config'e yalnız hash).
        builder.UseSetting("Platform:AdminUser", platform.Kullanici);
        builder.UseSetting("Platform:AdminPasswordHash", RentACar.Web.Platform.PlatformCredentials.HashPassword(platform.Sifre));
        builder.ConfigureTestServices(s =>
        {
            var jobs = s.Where(d => d.ServiceType == typeof(IHostedService)
                                     && d.ImplementationType?.Namespace?.StartsWith("RentACar", StringComparison.Ordinal) == true)
                         .ToList();
            foreach (var d in jobs) s.Remove(d);
            if (testEndpoints) s.AddSingleton<IUiApiEndpointRegistration, TestUiEndpoints>();
            // F11.1b güvenlik M6: alan adı TXT doğrulaması gerçek DNS'e çıkmaz.
            s.AddSingleton<RentACar.Application.TenantSettings.IDnsTxtResolver>(FakeDnsTxtResolver.Instance);
        });
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}

/// <summary>
/// Test-only <c>/api/ui/v1/test/*</c> uçları: servis istisnalarını (YetkiYok, Validation+Alan, Duplicate*,
/// Mukerrer, beklenmeyen) gerçek grup filtreleri (hata → CSRF → pilot) üzerinden fırlatır. OpenAPI'ye girmez.
/// </summary>
public sealed class TestUiEndpoints : IUiApiEndpointRegistration
{
    public const string HiddenDetail = "GIZLI-IC-AYRINTI-9C1";

    public void Map(RouteGroupBuilder v1)
    {
        var t = v1.MapGroup("/test").RequirePermission(Permission.OperationsWrite).ExcludeFromDescription();
        t.MapGet("/tamam", () => TypedResults.Ok(new { tamam = true }));
        t.MapPost("/yaz", () => TypedResults.Ok(new { yazildi = true }));
        t.MapGet("/yetki-yok", NoContent () => throw new NoPermissionException("Bu kayıt şube kapsamınız dışında."));
        t.MapPost("/dogrulama", NoContent () => throw new ValidationException("Plaka zorunludur.", "Plaka"));
        t.MapPost("/cakisma", NoContent () => throw new DuplicatePlakaException("34 ABC 123"));
        t.MapPost("/mukerrer", NoContent () => throw new DuplicateOperationException("Bu işlem zaten kaydedildi."));
        t.MapGet("/patla", NoContent () => throw new InvalidOperationException(HiddenDetail));
        t.MapGet("/finans", () => TypedResults.Ok(new { finans = true })).RequirePermission(Permission.FinanceWrite);
    }
}

/// <summary>Çalışma anında üretilen test kimliği (firma kodu, kullanıcı adı, parola). Repoda sabit parola YOK.</summary>
public sealed record TestKimlik(string Firma, string Kullanici, string Sifre, string AdSoyad);

/// <summary>
/// Tek Web host'u + ayrı test DB'si. Test kullanıcıları ve platform operatörü ÇALIŞMA ANINDA rastgele
/// parolayla üretilir (GitGuardian: testte sabit kimlik bilgisi olmamalı; seed kullanıcılarına dayanılmaz).
/// Kullanıcılar uygulamanın kendi <c>IPasswordHasher&lt;User&gt;</c>'ıyla (girişin doğruladığı hasher) yazılır.
/// Tohum: <see cref="PilotAdmin"/>/<see cref="PilotOperator"/> PİLOT firmada, <see cref="OtherAdmin"/> pilot
/// olmayan firmada. Paralellik kapalı: Web açılışı statik <c>FormSecurity.EnforceAntiforgery</c>'yi yazar.
/// </summary>
public sealed class WebFixture : IAsyncLifetime
{
    private readonly PostgresFixture _pg = new();
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), "racar-web-" + Guid.NewGuid().ToString("N"));
    private WebFactory? _narrowLimited;

    public WebFactory Web { get; private set; } = default!;
    public PostgresFixture Pg => _pg;
    public Guid PilotCompanyId { get; private set; }
    public string PilotCompanyName { get; } = "Pilot Test Firması";
    public TestKimlik PilotAdmin { get; private set; } = default!;
    public TestKimlik PilotOperator { get; private set; } = default!;
    public TestKimlik OtherAdmin { get; private set; } = default!;
    /// <summary>Platform operatörü kimliği (config'e hash'i yazılır; düz parola yalnız bellekte).</summary>
    public TestKimlik Platform { get; } = new("", "p" + Short(), RandomPassword(), "");

    /// <summary>Giriş ve istemci hata raporu limiti 2 olan ikinci host (429 testleri) — ilk erişimde kurulur.</summary>
    public WebFactory NarrowLimited => _narrowLimited ??= new WebFactory(_pg, Path.Combine(_temporary, "log-dar-.log"), Platform,
        loginLimit: 2, clientErrorLimit: 2);

    /// <summary>Ana host'un log dosyaları (Serilog CompactJson, <c>log-web-*.log</c>) bu dizinde.</summary>
    public string LogDirectory => _temporary;

    public static string RandomPassword() => Guid.NewGuid().ToString("N") + "Aa1!";
    private static string Short() => Guid.NewGuid().ToString("N")[..10];

    public async Task InitializeAsync()
    {
        await _pg.InitializeAsync();
        Directory.CreateDirectory(_temporary);
        Web = new WebFactory(_pg, Path.Combine(_temporary, "log-web-.log"), Platform);
        _ = Web.Server; // host'u başlat → migration + seed

        PilotAdmin = await CompanyAndUserAsync(PilotCompanyName, UserRole.Admin);
        PilotCompanyId = await TenantIdAsync(PilotAdmin.Firma);
        PilotOperator = await AddUserAsync(PilotCompanyId, PilotAdmin.Firma, UserRole.Operator, branch: "Merkez");
        await MakePilotAsync(PilotCompanyId, true);
        OtherAdmin = await CompanyAndUserAsync("Diğer Test Firması", UserRole.Admin);
    }

    /// <summary>Yeni firma + Admin/verilen roldeki kullanıcı (rastgele kod, ad ve parola).</summary>
    public async Task<TestKimlik> CompanyAndUserAsync(string companyName, UserRole rol = UserRole.Admin)
    {
        var code = "f" + Short();
        await using (var db = OwnerDb())
        {
            db.Tenants.Add(new Tenant { Code = code, Name = companyName, IsActive = true });
            await db.SaveChangesAsync();
        }
        return await AddUserAsync(await TenantIdAsync(code), code, rol);
    }

    /// <summary>Var olan firmaya kullanıcı: uygulamanın IPasswordHasher&lt;User&gt;'ı ile (Users platform tablosu, owner yazar).</summary>
    public async Task<TestKimlik> AddUserAsync(Guid tenantId, string companyCode, UserRole rol, string? branch = null)
    {
        var identity = new TestKimlik(companyCode, "k" + Short(), RandomPassword(), "Test " + rol);
        var user = new User
        {
            TenantId = tenantId, UserName = identity.Kullanici, DisplayName = identity.AdSoyad,
            Rol = rol, AtanmisSube = branch, IsActive = true,
        };
        user.PasswordHash = Web.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, identity.Sifre);
        await using var db = OwnerDb();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return identity;
    }

    private AppDbContext OwnerDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_pg.OwnerConnectionString).Options,
               NullTenantContext.Instance, NullCurrentUser.Instance);

    public async Task<Guid> TenantIdAsync(string code)
    {
        await using var c = new NpgsqlConnection(_pg.OwnerConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT \"Id\" FROM \"Tenants\" WHERE \"Code\" = @k", c);
        cmd.Parameters.AddWithValue("k", code);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Pilot bayrağı gerçek repository'den (RLS'li racar_app) yazılır — elle SQL değil.</summary>
    public async Task MakePilotAsync(Guid tenantId, bool pilot)
    {
        using var host = new TestHost(_pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId, role: UserRole.Admin);
        await scope.ServiceProvider.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(s => s.YeniArayuzPilot = pilot);
    }

    public async Task DisposeAsync()
    {
        if (_narrowLimited is not null) await _narrowLimited.DisposeAsync();
        await Web.DisposeAsync();
        await _pg.DisposeAsync();
        try { Directory.Delete(_temporary, recursive: true); } catch { /* best-effort */ }
    }
}

[CollectionDefinition("web", DisableParallelization = true)]
public sealed class WebCollection : ICollectionFixture<WebFixture>;
