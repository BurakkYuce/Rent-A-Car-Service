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
/// dar limitli host'unu kurar. <paramref name="testUclari"/>: hata sözleşmesini GERÇEK grup filtrelerinden
/// geçirmek için test-only uçlar (<see cref="TestUiUclari"/>) eklenir.
/// </summary>
public sealed class WebFactory(PostgresFixture pg, string logYolu, TestKimlik platform, int girisLimiti = 10_000, bool testUclari = true,
    int istemciHataLimiti = 10_000)
    : WebApplicationFactory<RentACar.Web.Common.DogrulamaHatasiMiddleware>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", pg.AppConnectionString);
        builder.UseSetting("ConnectionStrings:Migrator", pg.OwnerConnectionString);
        builder.UseSetting("Logging:FilePath", logYolu);
        builder.UseSetting("RateLimit:LoginPermit", girisLimiti.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimit:IstemciHataPermit", istemciHataLimiti.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // Platform operatörü: dev varsayılanı yerine çalışma anında üretilen kimlik (config'e yalnız hash).
        builder.UseSetting("Platform:AdminUser", platform.Kullanici);
        builder.UseSetting("Platform:AdminPasswordHash", RentACar.Web.Platform.PlatformCredentials.HashPassword(platform.Sifre));
        builder.ConfigureTestServices(s =>
        {
            var isler = s.Where(d => d.ServiceType == typeof(IHostedService)
                                     && d.ImplementationType?.Namespace?.StartsWith("RentACar", StringComparison.Ordinal) == true)
                         .ToList();
            foreach (var d in isler) s.Remove(d);
            if (testUclari) s.AddSingleton<IUiApiUcKaydi, TestUiUclari>();
            // F11.1b güvenlik M6: alan adı TXT doğrulaması gerçek DNS'e çıkmaz.
            s.AddSingleton<RentACar.Application.TenantSettings.IDnsTxtResolver>(FakeDnsTxtResolver.Instance);
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

/// <summary>Çalışma anında üretilen test kimliği (firma kodu, kullanıcı adı, parola). Repoda sabit parola YOK.</summary>
public sealed record TestKimlik(string Firma, string Kullanici, string Sifre, string AdSoyad);

/// <summary>
/// Tek Web host'u + ayrı test DB'si. Test kullanıcıları ve platform operatörü ÇALIŞMA ANINDA rastgele
/// parolayla üretilir (GitGuardian: testte sabit kimlik bilgisi olmamalı; seed kullanıcılarına dayanılmaz).
/// Kullanıcılar uygulamanın kendi <c>IPasswordHasher&lt;User&gt;</c>'ıyla (girişin doğruladığı hasher) yazılır.
/// Tohum: <see cref="PilotAdmin"/>/<see cref="PilotOperator"/> PİLOT firmada, <see cref="DigerAdmin"/> pilot
/// olmayan firmada. Paralellik kapalı: Web açılışı statik <c>FormSecurity.EnforceAntiforgery</c>'yi yazar.
/// </summary>
public sealed class WebFixture : IAsyncLifetime
{
    private readonly PostgresFixture _pg = new();
    private readonly string _gecici = Path.Combine(Path.GetTempPath(), "racar-web-" + Guid.NewGuid().ToString("N"));
    private WebFactory? _darLimitli;

    public WebFactory Web { get; private set; } = default!;
    public PostgresFixture Pg => _pg;
    public Guid PilotFirmaId { get; private set; }
    public string PilotFirmaAd { get; } = "Pilot Test Firması";
    public TestKimlik PilotAdmin { get; private set; } = default!;
    public TestKimlik PilotOperator { get; private set; } = default!;
    public TestKimlik DigerAdmin { get; private set; } = default!;
    /// <summary>Platform operatörü kimliği (config'e hash'i yazılır; düz parola yalnız bellekte).</summary>
    public TestKimlik Platform { get; } = new("", "p" + Kisa(), RastgeleParola(), "");

    /// <summary>Giriş ve istemci hata raporu limiti 2 olan ikinci host (429 testleri) — ilk erişimde kurulur.</summary>
    public WebFactory DarLimitli => _darLimitli ??= new WebFactory(_pg, Path.Combine(_gecici, "log-dar-.log"), Platform,
        girisLimiti: 2, istemciHataLimiti: 2);

    /// <summary>Ana host'un log dosyaları (Serilog CompactJson, <c>log-web-*.log</c>) bu dizinde.</summary>
    public string LogDizini => _gecici;

    public static string RastgeleParola() => Guid.NewGuid().ToString("N") + "Aa1!";
    private static string Kisa() => Guid.NewGuid().ToString("N")[..10];

    public async Task InitializeAsync()
    {
        await _pg.InitializeAsync();
        Directory.CreateDirectory(_gecici);
        Web = new WebFactory(_pg, Path.Combine(_gecici, "log-web-.log"), Platform);
        _ = Web.Server; // host'u başlat → migration + seed

        PilotAdmin = await FirmaVeKullaniciAsync(PilotFirmaAd, UserRole.Admin);
        PilotFirmaId = await TenantIdAsync(PilotAdmin.Firma);
        PilotOperator = await KullaniciEkleAsync(PilotFirmaId, PilotAdmin.Firma, UserRole.Operator, sube: "Merkez");
        await PilotYapAsync(PilotFirmaId, true);
        DigerAdmin = await FirmaVeKullaniciAsync("Diğer Test Firması", UserRole.Admin);
    }

    /// <summary>Yeni firma + Admin/verilen roldeki kullanıcı (rastgele kod, ad ve parola).</summary>
    public async Task<TestKimlik> FirmaVeKullaniciAsync(string firmaAd, UserRole rol = UserRole.Admin)
    {
        var kod = "f" + Kisa();
        await using (var db = OwnerDb())
        {
            db.Tenants.Add(new Tenant { Code = kod, Name = firmaAd, IsActive = true });
            await db.SaveChangesAsync();
        }
        return await KullaniciEkleAsync(await TenantIdAsync(kod), kod, rol);
    }

    /// <summary>Var olan firmaya kullanıcı: uygulamanın IPasswordHasher&lt;User&gt;'ı ile (Users platform tablosu, owner yazar).</summary>
    public async Task<TestKimlik> KullaniciEkleAsync(Guid tenantId, string firmaKod, UserRole rol, string? sube = null)
    {
        var kimlik = new TestKimlik(firmaKod, "k" + Kisa(), RastgeleParola(), "Test " + rol);
        var user = new User
        {
            TenantId = tenantId, UserName = kimlik.Kullanici, DisplayName = kimlik.AdSoyad,
            Rol = rol, AtanmisSube = sube, IsActive = true,
        };
        user.PasswordHash = Web.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, kimlik.Sifre);
        await using var db = OwnerDb();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return kimlik;
    }

    private AppDbContext OwnerDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_pg.OwnerConnectionString).Options,
               NullTenantContext.Instance, NullCurrentUser.Instance);

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
