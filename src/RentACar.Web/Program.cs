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
using RentACar.Web.Observability;
using RentACar.Web.Calendar;
using RentACar.Web.Import;
using RentACar.Web.Notifications;
using RentACar.Infrastructure.Integrations;
using RentACar.Web.Kur;
using RentACar.Web.Identity;
using RentACar.Web.Platform;
using RentACar.Web.SiteIcerik;
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
using RentACar.Web.RezSartlar;
using RentACar.Web.TarifeGruplari;
using RentACar.Web.Reports;
using RentACar.Web.ServiceRecords;
using RentACar.Web.TenantSettings;
using RentACar.Web.Personnel;
using RentACar.Web.Legal;
using RentACar.Web.Blog;
using RentACar.Web.PublicSite;
using RentACar.Web.Crm;
using RentACar.Web.Periods;
using RentACar.Web.Authorization;
using RentACar.Web.Notifications;
using RentACar.Infrastructure.Integrations;
using RentACar.Web.Users;
using RentACar.Web.VehicleGroups;
using RentACar.Web.Documents;
using RentACar.Web.WebSite;
using RentACar.Web.VehicleSales;
using RentACar.Web.FiloKiralamalar;
using RentACar.Web.AracSiparisleri;
using RentACar.Web.AracKredileri;
using RentACar.Web.Baflar;
using RentACar.Web.HesapKodlari;
using RentACar.Web.ServisTanimlari;
using RentACar.Web.DropTanimlari;
using RentACar.Web.FiloPlan;
using RentACar.Web.MusteriTaksitleri;
using RentACar.Web.DolulukFiyat;
using RentACar.Web.BelgeSablon;
using RentACar.Web.Vehicles;
using RentACar.Web.Api;

// Bootstrap yardımcısı: `dotnet run --project src/RentACar.Web -- --platform-hash '<parola>'` →
// Platform:AdminPasswordHash değerini (üretimde zorunlu) üretir ve çıkar (bkz. docs/ops/yedekleme.md §5.1).
if (args is ["--platform-hash", var bootstrapPw, ..])
{
    Console.WriteLine(PlatformCredentials.HashPassword(bootstrapPw));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// ---- Gözlemlenebilirlik (P0-2): yapılandırılmış log — konsol + günlük dönen dosya (14 gün saklama) ----
var otlpEndpointWeb = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddSerilog(lc =>
{
    lc.MinimumLevel.Information()
      .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
      .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
      .Enrich.FromLogContext()
      .WriteTo.Console()
      // Dosya sink JSON (CompactJsonFormatter): tenant_id/user/request_id birinci-sınıf alan.
      .WriteTo.File(new Serilog.Formatting.Compact.CompactJsonFormatter(),
          builder.Configuration["Logging:FilePath"] ?? "logs/rentacar-web-.log",
          rollingInterval: RollingInterval.Day,
          retainedFileCountLimit: 14);
    // OTLP log sink — config-gated: endpoint set ise loglar Collector'a (→ Loki). Trace-id ile korele.
    if (!string.IsNullOrWhiteSpace(otlpEndpointWeb))
        lc.WriteTo.OpenTelemetry(o =>
        {
            o.Endpoint = otlpEndpointWeb;
            o.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc;
            o.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = "rentacar-web" };
        });
});

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
// Kestrel "Server: Kestrel" başlığını gizle (parmak izi azaltma).
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);
// HSTS sertleştirme (in-app UseHsts bunu okur): 1 yıl + subdomain'ler. preload YOK (geri alması zor;
// hstspreload.org başvurusuz inert; tüm tenant subdomain'leri HTTPS olmalı → sonra).
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // Cookie sertleştirme: HttpOnly + SameSite=Lax açıkça; Secure PROD'da zorunlu (dev http://localhost
        // login'i kırılmasın diye SameAsRequest). Özel ad → framework parmak izini (.AspNetCore.Cookies) gizler.
        options.Cookie.Name = "racar.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.LoginPath = "/login";
        // 403 → /login DEĞİL: kullanıcı zaten girişli olduğu için Login.razor onu /'a atıyor ve
        // "durduk yere ana ekrana düştüm" oluyordu. Bkz. Identity/YetkiYonlendirme.cs.
        options.AccessDeniedPath = "/yetkisiz";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        // TEK şema; /platform alanı için login/access-denied AYRI sayfaya yönlendirilir (alan-bazlı).
        // 401: GET isteğinin asıl hedefi ?ReturnUrl= olarak taşınır (bildirim/WhatsApp derin bağlantısı
        // girişten sonra kaybolmasın); POST vb.'de taşınmaz. ctx.RedirectUri KULLANILMAZ — çerçevenin
        // hazırladığı adres yöntem ayrımı yapmaz. Karar saf fonksiyonda (test edilebilir):
        // YetkiYonlendirme.GirisYonlendirmesi.
        // Referer: asıl hedef bir İNDİRME ucuysa (Excel/CSV/PDF indir) dönüş o uç olamaz — dosya iner,
        // ekranda giriş formu kalır. Bu durumda kullanıcı indirmeyi başlattığı sayfaya döner
        // (YetkiYonlendirme.IndirmeAdresiMi); yalnız aynı kökenli Referer kullanılır.
        options.Events.OnRedirectToLogin = ctx =>
        {
            // F1.2: yeni arayüz API'si 302 İZLEMEZ (izlerse /login HTML'ini JSON diye okur) → 401 ProblemDetails.
            if (RentACar.Web.Api.UiApiExtensions.UiYolu(ctx.Request.Path))
                return RentACar.Web.Api.UiApiExtensions.YazAsync(ctx.HttpContext,
                    RentACar.Web.Api.UiHata.OturumYok, "Oturum açık değil ya da süresi doldu.");
            ctx.Response.Redirect(YetkiYonlendirme.GirisYonlendirmesi(
                ctx.Request.Method, ctx.Request.Path, ctx.Request.QueryString,
                YetkiYonlendirme.AyniKokenYolu(ctx.Request.Headers.Referer, ctx.Request.Host)));
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (RentACar.Web.Api.UiApiExtensions.UiYolu(ctx.Request.Path)) // F1.2: /yetkisiz HTML'i değil
                return RentACar.Web.Api.UiApiExtensions.YazAsync(ctx.HttpContext,
                    RentACar.Web.Api.UiHata.YetkiYok, "Bu işlem için yetkiniz yok.");
            ctx.Response.Redirect(YetkiYonlendirme.YetkisizHedefi(ctx.Request.Path));
            return Task.CompletedTask;
        };
    });
// PlatformAdmin policy: platform operatörü claim'i (tenant login'i ASLA yazmaz).
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(PlatformClaims.Policy, p => p.RequireClaim(PlatformClaims.PlatformAdmin, "true"));
    // İzin policy'leri (izin:OperationsWrite vb.): sayfa [Authorize(Policy=...)] attribute'ları
    // rol listesi yerine ETKİN izne bakar — kullanıcı-bazlı ek izin sayfayı AÇAR, yasak KAPATIR.
    o.AddPermissionPolicies();
});

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
    // Tenant girişinde formdaki dönüş adresi (ReturnUrl) korunur: sınıra takılan kullanıcı bir dakika
    // sonraki doğru girişte derin bağlantısını kaybetmesin (HataliGirisHedefi ile aynı gerekçe). Form
    // YALNIZ küçükse okunur — giriş formu birkaç yüz bayttır; reddedilen isteğin büyük gövdesini
    // belleğe almak brute-force'u bellek saldırısına çevirirdi. Değer GüvenliDonus'tan geçer.
    o.OnRejected = async (ctx, ct) =>
    {
        RentACar.Application.Observability.RacarMetrics.RateLimitRejected("login"); // metrik: rate-limit reddi
        var req = ctx.HttpContext.Request;
        // F1.2: yeni arayüz API'si yönlendirme değil 429 ProblemDetails alır (SPA formu korur, bekletir).
        if (RentACar.Web.Api.UiApiExtensions.UiYolu(req.Path))
        {
            await RentACar.Web.Api.UiApiExtensions.YazAsync(ctx.HttpContext,
                RentACar.Web.Api.UiHata.CokIstek, "Çok fazla deneme; biraz sonra tekrar deneyin.");
            return;
        }
        if (req.Path.StartsWithSegments("/platform"))
        {
            ctx.HttpContext.Response.Redirect("/platform/login?hata=limit");
            return;
        }
        string? donus = null;
        if (req.HasFormContentType && req.ContentLength is > 0 and <= 8 * 1024)
        {
            try { donus = (await req.ReadFormAsync(ct))[YetkiYonlendirme.DonusParametresi]; }
            catch (Exception ex) when (ex is InvalidDataException or IOException
                                          or BadHttpRequestException or OperationCanceledException)
            { donus = null; } // bozuk/yarım gövde: dönüşsüz limit sayfası yeter
        }
        ctx.HttpContext.Response.Redirect(YetkiYonlendirme.LimitHedefi(donus));
    };
});

// F1.2: /api/ui/v1 — antiforgery başlık adı (X-XSRF-TOKEN) + OpenAPI (yalnız Development).
builder.Services.AddUiApi(builder.Environment);

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
    platformHash ??= PlatformCredentials.HashPassword("platform1376"); // dev varsayılan (config'te yoksa)
}
else if (string.IsNullOrWhiteSpace(platformUser) || string.IsNullOrWhiteSpace(platformHash))
    throw new InvalidOperationException(
        "Platform:AdminUser + Platform:AdminPasswordHash bu ortamda zorunludur (platform süper-admin kimliği).");
builder.Services.AddSingleton(new PlatformCredentials(platformUser!, platformHash!));
builder.Services.AddMemoryCache();
builder.Services.AddScoped<PlatformAdminService>();
builder.Services.AddScoped<TenantStatusCache>();
builder.Services.AddScoped<TenantActiveMiddleware>(); // anlık kesme (IMiddleware)
builder.Services.AddScoped<PlatformIsolationMiddleware>(); // platform admin → tenant sayfası ayrımı
builder.Services.AddScoped<RentACar.Web.Common.DogrulamaHatasiMiddleware>(); // ValidationException → 500 DEĞİL, kullanıcıya gösterilebilir hata
builder.Services.AddScoped<RentACar.Web.Observability.RequestEnrichment.Middleware>(); // log: tenant/user/req-id

// Readiness health-check'leri (tag "ready"): DB (CanConnect) + Migrator + DataProtection keyring.
builder.Services.AddHealthChecks()
    .AddCheck<RentACar.Web.Observability.DbConnectHealthCheck>("db", tags: ["ready"])
    .AddCheck<RentACar.Web.Observability.MigratorConnectHealthCheck>("migrator", tags: ["ready"])
    .AddCheck<RentACar.Web.Observability.KeyringHealthCheck>("keyring", tags: ["ready"]);

// OpenTelemetry metrik + trace (OTLP exporter config-gated: OTEL_EXPORTER_OTLP_ENDPOINT set ise).
builder.Services.AddRacarObservability(builder.Configuration, "rentacar-web");

// iCal takvim feed (kimliksiz abonelik) + token yönetimi (owner conn).
builder.Services.AddScoped<CalendarFeedService>();
// PR-C: anonim sözleşme görüntüleme (iki-fazlı tenant çözümü — CalendarFeedService ile aynı desen).
builder.Services.AddScoped<RentACar.Web.Bookings.SozlesmeGoruntuleService>();
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
builder.Services.AddHostedService<RentACar.Web.Jobs.DonemFaturaJob>(); // FAZ 4.2-B4: dönemsel fatura job'ı (ayar-kapılı)
builder.Services.AddHttpClient(); // TCMB kur çekimi
builder.Services.AddSingleton<RentACar.Web.Kur.TcmbKurService>(); // TCMB kur çek/upsert (paylaşımlı KurKayitlari)
builder.Services.AddHostedService<RentACar.Web.Jobs.TcmbKurJob>(); // scheduler: TCMB günlük kur (kimliksiz)
builder.Services.AddHostedService<RentACar.Web.Jobs.OpsWatchdogJob>(); // Grafana'sız kritik alarm (config-gated: Twilio:AlertPhone)
builder.Services.AddHostedService<RentACar.Web.Jobs.PendingDomainExpireJob>(); // PR-5: ACME kota-koruması (48s süre aşımı)
// WhatsApp: Twilio config VARSA gerçek gönderici stub'ı override eder (son kayıt kazanır); yoksa stub no-op kalır.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Twilio:AccountSid"]))
    builder.Services.AddSingleton<RentACar.Application.Integrations.IWhatsAppService, RentACar.Web.Integrations.TwilioWhatsAppService>();
// SMS: aynı Twilio hesabı. Kimlik + bir gönderen kaynağı (numara ya da Messaging Service) gerekir;
// tenant kendi başlığını verirse o kazanır ama hesap kimliği yine de şarttır → kapı burada.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Twilio:AccountSid"])
    && (!string.IsNullOrWhiteSpace(builder.Configuration["Twilio:SmsFrom"])
        || !string.IsNullOrWhiteSpace(builder.Configuration["Twilio:MessagingServiceSid"])))
    builder.Services.AddSingleton<RentACar.Application.Integrations.ISmsService, RentACar.Web.Integrations.TwilioSmsService>();
// Ödeme: iyzico config VARSA gerçek adaptör stub'ı override eder; yoksa stub dürüstçe
// "yapılandırılmadı" döner (sahte işlem referansı ÜRETMEZ).
builder.Services.AddIyzico(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddScoped<RentACar.Web.Reports.ReportExportService>(); // roadmap B1: rapor export
builder.Services.AddSingleton<RentACar.Web.Reports.PdfExportService>(); // roadmap F4: PDF export
builder.Services.AddScoped<RentACar.Web.Import.ImportService>(); // veri göçü: Excel/CSV → araç/cari (PII şifreli)

// LoginService + IPasswordHasher<User> + Application IPasswordHasher köprüsü artık
// AddInfrastructure'da (Web cookie + API JWT ortak kullanır).

// FAZ 2.2: Tut/Sat eşikleri appsettings "TutSat" bölümünden override edilebilir (yoksa varsayılan).
if (builder.Configuration.GetSection("TutSat").Exists())
    builder.Services.AddSingleton(new RentACar.Application.Reporting.TutSatEsikleri(
        builder.Configuration.GetValue("TutSat:DegerOrani", 0.45m),
        builder.Configuration.GetValue("TutSat:SinifKati", 1.5m)));

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
// F1.2: /api/ui için no-store + StatusCodePages/istisna HTML'i yerine ProblemDetails (ikisinin İÇİNDE durmalı).
app.UseUiApiBoruHatti();

app.UseRequestLocalization(); // tr-TR (yukarıda Configure edildi) — Radzen tarih/sayı formatı tutarlı

// Reverse-proxy (Caddy/nginx aynı makinede) arkasında gerçek istemci IP'si — rate limit doğru IP'yi görsün.
// Varsayılan KnownProxies=loopback: uzak istemciden gelen sahte X-Forwarded-For'a güvenilmez.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

// ---- Güvenlik yanıt başlıkları (defense-in-depth: Caddy'den bağımsız, HER yanıtta) ----
// CSP script-src 'self' (inline event handler'lar harici JS'e taşındı → rc-ui.js). style-src 'unsafe-inline'
// bilinçli pragmatik: inline style attr'ları/blokları kalıyor (XSS riski script'e göre düşük; tümünü ayıklamak
// devasa iş). connect-src 'self' → Blazor/Radzen interaktif SignalR (same-origin ws) çalışır. frame-ancestors
// 'self' + X-Frame SAMEORIGIN → PDF-yazdır gizli iframe'i (same-origin) çalışır. object-src 'none'.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "SAMEORIGIN";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "geolocation=(), camera=(), microphone=(), payment=()";
    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        // script-src 'self' — HİÇBİR inline script yok (event handler'lar rc-ui.js'e taşındı; Blazor'ın
        // <ImportMap> inline script'i App.razor'dan kaldırıldı — tam statik SSR'de gereksizdi ve importmap
        // fingerprint'i her asset değişiminde dönüp hash'i bozuyordu). 'unsafe-inline' YOK, hash YOK → temiz
        // ve kırılgan-değil (asset/CSS değişimleri artık CSP'yi bozmaz).
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'self'; " +
        "base-uri 'self'; " +
        "object-src 'none'";
    await next();
});
app.UseSerilogRequestLogging(options => // istek başına tek satır: metot, yol, durum, süre + tenant/user/req-id
{
    // Takvim feed URL'i TOKEN içerir → request log'a DÜŞMESİN (log erişimi olan feed'e erişmesin, adversarial).
    // Verbose, Information min-level'ın altında → düşürülür; hata yine Error'da loglanır.
    options.GetLevel = (http, _, ex) =>
        ex is not null ? LogEventLevel.Error
        : http.Request.Path.StartsWithSegments("/feed/calendar") ? LogEventLevel.Verbose
        : LogEventLevel.Information;
    // Çok-kiracılı korelasyon: tamamlanma-olayına tenant_id/user/request_id ekle (User bu noktada dolu).
    options.EnrichDiagnosticContext = RentACar.Web.Observability.RequestEnrichment.Enrich;
});
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
// Log zenginleştirme (auth SONRASI — claim'ler dolu): sonraki tüm istek-içi loglar tenant/user/req-id taşır.
// ValidationException'ı sayfa yolunda da yakala (POST uçları kendi ?hata= yolunu kullanıyor;
// sayfa render'ında yakalayan yoktu → kullanıcı 500 görüyordu). Aşağıdaki her şeyi sarar.
app.UseMiddleware<RentACar.Web.Common.DogrulamaHatasiMiddleware>();
app.UseMiddleware<RentACar.Web.Observability.RequestEnrichment.Middleware>();
// Anlık kesme: kapatılan tenant'ın authenticated isteği (açık oturum) bir sonraki istekte /login'e düşer.
app.UseMiddleware<TenantActiveMiddleware>();
app.UseMiddleware<PlatformIsolationMiddleware>(); // platform operatörü tenant UI'ına giremez (konsola yönlendir)
app.UseAntiforgery();

// roadmap E2: antiforgery yalnız PROD'da zorunlu (dev/test gevşek). Map'lerden ÖNCE set edilir
// (AntiforgeryByEnv build-time okur). Formlar <AntiforgeryToken/> taşır → prod'da CSRF korumalı.
// Antiforgery (adversarial): Pii/DP-key guard deseniyle TUTARLI — Development DIŞINDA her ortamda AÇIK.
// Önceden IsProduction()'dı → Staging (gerçek veri taşıyabilir) CSRF'e AÇIK kalıyordu. Dev-off/prod-on korunur.
RentACar.Web.Identity.FormSecurity.EnforceAntiforgery = !app.Environment.IsDevelopment();

// Sağlık — liveness/readiness ayrımı (MS deseni). Anonim (uptime monitörü/proxy ping'i).
//  • /health/live  → süreç ayakta mı (bağımlılık kontrolü YOK; orchestrator restart sinyali).
//  • /health/ready → trafik almaya hazır mı: DB (CanConnect) + Migrator + keyring (tag "ready").
//  • /health       → geriye-uyum: readiness'e alias.
var healthReady = new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready"),
    ResponseWriter = RentACar.Web.Observability.HealthResponse.Write, // {status:healthy/unhealthy} sözleşmesi
};
app.MapHealthChecks("/health/live", new()
{
    Predicate = _ => false,
    ResponseWriter = RentACar.Web.Observability.HealthResponse.Write,
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", healthReady).AllowAnonymous();
app.MapHealthChecks("/health", healthReady).AllowAnonymous(); // geriye-uyum

// Grafana Alerting köprüsü: alarmı KENDİ loglarımıza (Warning → Loki/dosya) yazar + best-effort WhatsApp.
// Gizli-anahtar kapılı (config Observability:AlertToken yoksa uç KAPALI — açık uç bırakma). Makine POST'u
// → anonim + antiforgery muaf; anonim olduğundan TenantActive/PlatformIsolation zaten dokunmaz.
app.MapPost("/internal/alert", async (HttpContext ctx, IConfiguration cfg, IServiceProvider sp, ILoggerFactory lf) =>
{
    // Makine-uç (Grafana webhook): HTML hata-sayfası re-execute'ini KAPAT → çağıran ham durum kodunu görsün.
    // Aksi halde boş-gövdeli 401/404, StatusCodePagesWithReExecute("/not-found") ile POST /not-found'a
    // yeniden çalışıp antiforgery'e takılıyor ve istemciye 400 "incorrect Content-type" dönüyor (handler 401 verse de).
    var scp = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>();
    if (scp is not null) scp.Enabled = false;

    var configured = cfg["Observability:AlertToken"];
    if (string.IsNullOrWhiteSpace(configured)) return Results.NotFound();
    var token = RentACar.Web.Observability.AlertWebhook.ExtractToken(
        ctx.Request.Headers["X-Alert-Token"].ToString(), ctx.Request.Headers.Authorization.ToString());
    if (!RentACar.Web.Observability.AlertWebhook.Authorized(token, configured))
        return Results.Unauthorized();

    string body;
    using (var reader = new StreamReader(ctx.Request.Body)) body = await reader.ReadToEndAsync();
    var summary = RentACar.Web.Observability.AlertWebhook.Summarize(body);
    lf.CreateLogger("OpsAlert").LogWarning("OPS ALERT: {Summary}", summary);

    var wa = sp.GetService<RentACar.Application.Integrations.IWhatsAppService>();
    var phone = cfg["Twilio:AlertPhone"];
    if (wa is not null && !string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(cfg["Twilio:Templates:ops_alert"]))
        try { await wa.SendTemplateAsync(phone, "ops_alert", new Dictionary<string, string> { ["1"] = summary }); }
        catch { /* alarm forward hatası alarmı düşürmez */ }

    return Results.Ok();
}).AllowAnonymous().DisableAntiforgery();

app.MapStaticAssets();
// F1.5 — yeni arayüz kabuğu /app altında ANONİM (cookie challenge yok → /login döngüsü yok); Spa:Dizin
// content root'a göreli (varsayılan ../app/browser). Güvenlik başlıkları/CSP yukarıdaki genel middleware'den.
RentACar.Web.Spa.SpaBarindirma.MapSpaBarindirma(app);
app.MapAuthEndpoints();
app.MapUiApi();                   // F1.2 — /api/ui/v1 (yeni arayüz JSON katmanı: CSRF + pilot kapısı + ProblemDetails)
app.MapPlatformAuthEndpoints();   // platform operatörü login/logout
app.MapPlatformTenantEndpoints(); // tenant aç/kapa/oluştur (PlatformAdmin policy)
app.MapPlatformBelgeEndpoints();  // PR-B — Belge Merkezi (PlatformAdmin policy)
app.MapCalendarFeedEndpoints();   // kimliksiz iCal feed + token yenile
app.MapKurEndpoints();            // TCMB yenile + sabit kur CRUD
app.MapVehicleEndpoints();
app.MapVehiclePhotoEndpoints(); // PR-3
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
app.MapDolulukFiyatEndpoints(); // FAZ 3.A7
app.MapBelgeSablonEndpoints(); // marka-özel PDF metin şablonları
app.MapDamageFileEndpoints();
app.MapServiceRecordEndpoints();
app.MapUserEndpoints();
app.MapProfileEndpoints();   // FAZ-83: kendi parolanı değiştirme (rol kısıtı YOK)
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
app.MapImportEndpoints(); // veri göçü içe-aktarım (ManageUsers)
app.MapTenantSettingsEndpoints();
app.MapMesajSablonEndpoints();
app.MapPersonelEndpoints();
app.MapHukukEndpoints();
app.MapCrmEndpoints();
app.MapAssistansTalepEndpoints();   // FAZ-44
app.MapFiloPlanEndpoints();         // FAZ-19
app.MapMusteriTaksitEndpoints();    // FAZ-66
app.MapBlogEndpoints(); // PR-6 — halka açık site blog yönetimi
app.MapGelenTalepEndpoints(); // PR-8 — site talepleri (lead) dönüştür/reddet
app.MapWebSiteEndpoints();    // PR-12 — Web Sitesi modülü (modül+rol kapılı)
app.MapFirmaBelgeEndpoints(); // PR-B — tenant tarafı belge indirme (salt-okur, dört koşullu)
app.MapFirmaDokumanEndpoints(); // firmanın KENDİ yüklediği PDF dokümanları (yükle/sil/indir)
app.MapSozlesmePaylasimEndpoints();  // PR-C — paylaş / yeni sürüm / iptal (girişli, OperationsWrite)
app.MapSozlesmeGoruntuleEndpoints(); // PR-C — GET /sozlesme/{token} ANONİM (ERP host'unda, PublicSite'ta değil)
app.MapSiteIcerikEndpoints();        // PR-16 — halka açık site içerik sayfaları + SSS yönetimi
app.MapDonemKapanisEndpoints();
app.MapYetkiEndpoints();
app.MapBildirimEndpoints();
app.MapRateMatrixEndpoints();
app.MapCoverageProductEndpoints();
app.MapRentalRuleEndpoints();
app.MapBrokerYasakEndpoints();
app.MapRezSartEndpoints();
app.MapVardiyaEndpoints();   // FAZ-45
app.MapTarifeGrubuEndpoints();
app.MapQuoteEndpoints();
app.MapMaliyetTeklifiEndpoints();   // FAZ-74 — kayıtlı maliyet teklifi (deftere yazmaz)
app.MapEkHizmetEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
