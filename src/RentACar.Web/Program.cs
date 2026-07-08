using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RentACar.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Radzen;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using RentACar.Application;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure;
using RentACar.Web.Components;
using RentACar.Web.Calendar;
using RentACar.Web.Kur;
using RentACar.Web.Identity;
using RentACar.Web.Platform;
using RentACar.Web.Bookings;
using RentACar.Web.Branches;
using RentACar.Web.FuelKinds;
using RentACar.Web.TransmissionTypes;
using RentACar.Web.VehicleColors;
using RentACar.Web.CustomerGroups;
using RentACar.Web.InsuranceCompanies;
using RentACar.Web.Banks;
using RentACar.Web.Departments;
using RentACar.Web.PaymentTypes;
using RentACar.Web.Countries;
using RentACar.Web.Accessories;
using RentACar.Web.CancelReasons;
using RentACar.Web.ReservationSources;
using RentACar.Web.VehicleSegments;
using RentACar.Web.VehicleTypes;
using RentACar.Web.VehicleOwners;
using RentACar.Web.ExpenseCategories;
using RentACar.Web.FinancialAccounts;
using RentACar.Web.CustomCodes;
using RentACar.Web.Brands;
using RentACar.Web.Currencies;
using RentACar.Web.Customers;
using RentACar.Web.DamageFiles;
using RentACar.Web.EkHizmetler;
using RentACar.Web.Expenses;
using RentACar.Web.Finance;
using RentACar.Web.GelenEFaturalar;
using RentACar.Web.KdvRates;
using RentACar.Web.Locations;
using RentACar.Web.Penalties;
using RentACar.Web.PenaltyTypes;
using RentACar.Web.Persistence;
using RentACar.Web.Pricing;
using RentACar.Web.Regulation;
using RentACar.Web.CoverageProducts;
using RentACar.Web.RateMatrices;
using RentACar.Web.RentalRules;
using RentACar.Web.BrokerYasaklari;
using RentACar.Web.Reports;
using RentACar.Web.ServiceRecords;
using RentACar.Web.TenantSettings;
using RentACar.Web.Personnel;
using RentACar.Web.Legal;
using RentACar.Web.Crm;
using RentACar.Web.Periods;
using RentACar.Web.Authorization;
using RentACar.Web.Notifications;
using RentACar.Web.Users;
using RentACar.Web.VehicleGroups;
using RentACar.Web.VehicleSales;
using RentACar.Web.FiloKiralamalar;
using RentACar.Web.AracSiparisleri;
using RentACar.Web.AracKredileri;
using RentACar.Web.Baflar;
using RentACar.Web.HesapKodlari;
using RentACar.Web.ServisTanimlari;
using RentACar.Web.DropTanimlari;
using RentACar.Web.Vehicles;

// Bootstrap yardımcısı: `dotnet run --project src/RentACar.Web -- --platform-hash '<parola>'` →
// Platform:AdminPasswordHash değerini (üretimde zorunlu) üretir ve çıkar (bkz. docs/ops/yedekleme.md §5.1).
if (args is ["--platform-hash", var bootstrapPw, ..])
{
    Console.WriteLine(PlatformCredentials.HashPassword(bootstrapPw));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// ---- Gözlemlenebilirlik (P0-2): yapılandırılmış log — konsol + günlük dönen dosya (14 gün saklama) ----
builder.Services.AddSerilog(lc => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        builder.Configuration["Logging:FilePath"] ?? "logs/rentacar-web-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

// ---- Bağlantılar: Default = racar_app (RLS uygulanan runtime), Migrator = racar_owner (DDL/seed) ----
var appConn = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
var migratorConn = builder.Configuration.GetConnectionString("Migrator")
    ?? throw new InvalidOperationException("ConnectionStrings:Migrator eksik.");

// ---- Blazor (HİBRİT: static SSR taban + ağır grid ekranları @rendermode InteractiveServer) ----
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ---- Radzen (back-office interaktif grid + servisler: Notification/Dialog/Tooltip/Context) ----
builder.Services.AddRadzenComponents();

// ---- Kültür: tr-TR (tarih dd.MM.yyyy, ondalık virgül, TRY) — RadzenDatePicker/Numeric bunu kullanır.
// Mevcut static form-POST yolu FormParse'ta explicit InvariantCulture kullanır → ETKİLENMEZ (bağımsız).
var trCulture = new CultureInfo("tr-TR");
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture(trCulture);
    o.SupportedCultures = new[] { trCulture };
    o.SupportedUICultures = new[] { trCulture };
});

// ---- Kimlik / yetki (cookie, 2 aşamalı login) ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        // TEK şema; /platform alanı için login/access-denied AYRI sayfaya yönlendirilir (alan-bazlı).
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.Redirect(ctx.Request.Path.StartsWithSegments("/platform") ? "/platform/login" : "/login");
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.Redirect(ctx.Request.Path.StartsWithSegments("/platform") ? "/platform/login" : "/login");
            return Task.CompletedTask;
        };
    });
// PlatformAdmin policy: platform operatörü claim'i (tenant login'i ASLA yazmaz).
builder.Services.AddAuthorization(o =>
    o.AddPolicy(PlatformClaims.Policy, p => p.RequireClaim(PlatformClaims.PlatformAdmin, "true")));

// ---- Login brute-force koruması (P0): IP başına sabit-pencere limiti (yalnız /auth/login) ----
var loginPermit = builder.Configuration.GetValue("RateLimit:LoginPermit", 10);
var loginWindowSec = builder.Configuration.GetValue("RateLimit:LoginWindowSeconds", 60);
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPermit,
            Window = TimeSpan.FromSeconds(loginWindowSec),
            QueueLimit = 0,
        }));
    // SSR form akışı: 429 gövdesi yerine login sayfasına anlamlı mesajla dön (PRG deseniyle tutarlı).
    // /platform login'i AYRI sayfaya (adversarial L2: PlatformLogin'deki hata=limit dalı ölü olmasın).
    o.OnRejected = (ctx, _) =>
    {
        var target = ctx.HttpContext.Request.Path.StartsWithSegments("/platform")
            ? "/platform/login?hata=limit" : "/login?hata=limit";
        ctx.HttpContext.Response.Redirect(target);
        return ValueTask.CompletedTask;
    };
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, SsrAuthenticationStateProvider>();

// ITenantContext / ICurrentUser → RENDER MODUNA göre çözülür:
//  • static SSR (HttpContext VAR)      → HttpContextIdentity (claim'i her erişimde taze okur)
//  • interaktif circuit (HttpContext null) → CircuitTenantContext (circuit init'te bir kez doldurulur)
// Alt katman (interceptor/RLS/factory) değişmez — yalnız kimlik kaynağı mod-uyumlu olur.
builder.Services.AddScoped<CircuitTenantContext>();
builder.Services.AddScoped<HybridIdentity>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HybridIdentity>());
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<HybridIdentity>());

// Kabuk durumu (sidebar collapse / aktif menü) — layout seviyesi seam.
builder.Services.AddScoped<RentACar.Web.Components.Layout.ShellState>();

// ---- Platform süper-admin (tenant'tan bağımsız operatör konsolu) ----
// Kimlik config'ten; ÜRETİMDE ZORUNLU (Pii:HmacKey deseni — yoksa açılış reddeder, arka kapı yok).
var platformUser = builder.Configuration["Platform:AdminUser"];
var platformHash = builder.Configuration["Platform:AdminPasswordHash"];
if (builder.Environment.IsDevelopment())
{
    platformUser ??= "admin";
    platformHash ??= PlatformCredentials.HashPassword("***REMOVED***"); // dev varsayılan (config'te yoksa)
}
else if (string.IsNullOrWhiteSpace(platformUser) || string.IsNullOrWhiteSpace(platformHash))
    throw new InvalidOperationException(
        "Platform:AdminUser + Platform:AdminPasswordHash bu ortamda zorunludur (platform süper-admin kimliği).");
builder.Services.AddSingleton(new PlatformCredentials(platformUser!, platformHash!));
builder.Services.AddMemoryCache();
builder.Services.AddScoped<PlatformAdminService>();
builder.Services.AddScoped<TenantStatusCache>();
builder.Services.AddScoped<TenantActiveMiddleware>(); // anlık kesme (IMiddleware)

// iCal takvim feed (kimliksiz abonelik) + token yönetimi (owner conn).
builder.Services.AddScoped<CalendarFeedService>();
builder.Services.AddScoped<CalendarTokenService>();

// ---- Uygulama + altyapı ----
// PII blind-index anahtarı (KVKK/F2): Development DIŞINDA her ortamda ZORUNLU (Staging dahil —
// adversarial M1: gerçek PII'lı ortam bilinen dev anahtarına sessizce düşmemeli).
var piiKey = builder.Configuration["Pii:HmacKey"];
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(piiKey))
    throw new InvalidOperationException("Pii:HmacKey bu ortamda zorunludur (PII blind-index anahtarı).");
// DataProtection key-ring KALICI dizini (adversarial M1): üretimde zorunlu. Geçici FS'te (mount'suz container)
// key-ring her redeploy'da yenilenir → önceki *Enc PII kalıcı ÇÖZÜLEMEZ (sessiz veri kaybı). Guard = enforce.
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RACAR_DP_KEYS")))
    throw new InvalidOperationException("RACAR_DP_KEYS bu ortamda zorunludur (DataProtection key-ring kalıcı dizini; yoksa redeploy'da PII çözülemez).");
builder.Services.AddApplication();
builder.Services.AddInfrastructure(appConn, piiKey);
builder.Services.AddHostedService<RentACar.Web.Jobs.VadeBildirimJob>(); // scheduler: vade→bildirim (kimliksiz)
builder.Services.AddHttpClient(); // TCMB kur çekimi
builder.Services.AddSingleton<RentACar.Web.Kur.TcmbKurService>(); // TCMB kur çek/upsert (paylaşımlı KurKayitlari)
builder.Services.AddHostedService<RentACar.Web.Jobs.TcmbKurJob>(); // scheduler: TCMB günlük kur (kimliksiz)
// WhatsApp: Twilio config VARSA gerçek gönderici stub'ı override eder (son kayıt kazanır); yoksa stub no-op kalır.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Twilio:AccountSid"]))
    builder.Services.AddSingleton<RentACar.Application.Integrations.IWhatsAppService, RentACar.Web.Integrations.TwilioWhatsAppService>();
builder.Services.AddScoped<RentACar.Web.Reports.ReportExportService>(); // roadmap B1: rapor export
builder.Services.AddSingleton<RentACar.Web.Reports.PdfExportService>(); // roadmap F4: PDF export

// LoginService + IPasswordHasher<User> + Application IPasswordHasher köprüsü artık
// AddInfrastructure'da (Web cookie + API JWT ortak kullanır).

var app = builder.Build();

// ---- Şema + seed (owner bağlantısı) ----
await DbInitializer.MigrateAndSeedAsync(app.Services, migratorConn);

// ---- Pipeline ----
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseRequestLocalization(); // tr-TR (yukarıda Configure edildi) — Radzen tarih/sayı formatı tutarlı

// Reverse-proxy (Caddy/nginx aynı makinede) arkasında gerçek istemci IP'si — rate limit doğru IP'yi görsün.
// Varsayılan KnownProxies=loopback: uzak istemciden gelen sahte X-Forwarded-For'a güvenilmez.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
app.UseSerilogRequestLogging(options => // istek başına tek satır: metot, yol, durum, süre
{
    // Takvim feed URL'i TOKEN içerir → request log'a DÜŞMESİN (log erişimi olan feed'e erişmesin, adversarial).
    // Verbose, Information min-level'ın altında → düşürülür; hata yine Error'da loglanır.
    options.GetLevel = (http, _, ex) =>
        ex is not null ? LogEventLevel.Error
        : http.Request.Path.StartsWithSegments("/feed/calendar") ? LogEventLevel.Verbose
        : LogEventLevel.Information;
});
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
// Anlık kesme: kapatılan tenant'ın authenticated isteği (açık oturum) bir sonraki istekte /login'e düşer.
app.UseMiddleware<TenantActiveMiddleware>();
app.UseAntiforgery();

// roadmap E2: antiforgery yalnız PROD'da zorunlu (dev/test gevşek). Map'lerden ÖNCE set edilir
// (AntiforgeryByEnv build-time okur). Formlar <AntiforgeryToken/> taşır → prod'da CSRF korumalı.
// Antiforgery (adversarial): Pii/DP-key guard deseniyle TUTARLI — Development DIŞINDA her ortamda AÇIK.
// Önceden IsProduction()'dı → Staging (gerçek veri taşıyabilir) CSRF'e AÇIK kalıyordu. Dev-off/prod-on korunur.
RentACar.Web.Identity.FormSecurity.EnforceAntiforgery = !app.Environment.IsDevelopment();

// Sağlık (readiness, P0-2): DB'ye app rolüyle bağlanılabiliyor mu? Anonim (uptime monitörü/proxy ping'i).
app.MapGet("/health", async (IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
{
    try
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "healthy" })
            : Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

app.MapStaticAssets();
app.MapAuthEndpoints();
app.MapPlatformAuthEndpoints();   // platform operatörü login/logout
app.MapPlatformTenantEndpoints(); // tenant aç/kapa/oluştur (PlatformAdmin policy)
app.MapCalendarFeedEndpoints();   // kimliksiz iCal feed + token yenile
app.MapKurEndpoints();            // TCMB yenile + sabit kur CRUD
app.MapVehicleEndpoints();
app.MapCustomerEndpoints();
app.MapBookingEndpoints();
app.MapRentalAddOnEndpoints();
app.MapQuotationEndpoints();
app.MapFinanceEndpoints();
app.MapGelenEFaturaEndpoints();
app.MapDepozitoEndpoints(); // roadmap I3
app.MapExpenseEndpoints();
app.MapRegulationEndpoints();
app.MapPenaltyEndpoints();
app.MapVehicleSaleEndpoints();
app.MapFiloKiralamaEndpoints(); // roadmap L1
app.MapAracSiparisEndpoints(); // roadmap L3
app.MapAracKrediEndpoints(); // roadmap L4
app.MapBafEndpoints(); // roadmap L5
app.MapHesapKoduEndpoints(); // roadmap N1
app.MapServisTanimEndpoints(); // roadmap N1
app.MapDropTanimEndpoints(); // roadmap N2
app.MapDamageFileEndpoints();
app.MapServiceRecordEndpoints();
app.MapUserEndpoints();
app.MapBranchEndpoints();
app.MapRateCardEndpoints();
app.MapLocationEndpoints();
app.MapFuelKindEndpoints();
app.MapTransmissionTypeEndpoints();
app.MapVehicleColorEndpoints();
app.MapCustomerGroupEndpoints();
app.MapInsuranceCompanyEndpoints();
app.MapBankEndpoints();
app.MapDepartmentEndpoints();
app.MapPaymentTypeEndpoints();
app.MapCountryEndpoints();
app.MapAccessoryEndpoints();
app.MapCancelReasonEndpoints();
app.MapReservationSourceEndpoints();
app.MapVehicleSegmentEndpoints();
app.MapVehicleTypeEndpoints();
app.MapVehicleOwnerEndpoints();
app.MapExpenseCategoryEndpoints();
app.MapFinancialAccountEndpoints();
app.MapCustomCodeEndpoints();
app.MapBrandEndpoints();
app.MapCurrencyEndpoints();
app.MapPenaltyTypeEndpoints();
app.MapKdvRateEndpoints();
app.MapVehicleGroupEndpoints();
app.MapReportExportEndpoints();
app.MapListExportEndpoints(); // roadmap G6: liste export
app.MapPdfEndpoints();
app.MapTenantSettingsEndpoints();
app.MapPersonelEndpoints();
app.MapHukukEndpoints();
app.MapCrmEndpoints();
app.MapDonemKapanisEndpoints();
app.MapYetkiEndpoints();
app.MapBildirimEndpoints();
app.MapRateMatrixEndpoints();
app.MapCoverageProductEndpoints();
app.MapRentalRuleEndpoints();
app.MapBrokerYasakEndpoints();
app.MapQuoteEndpoints();
app.MapEkHizmetEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
