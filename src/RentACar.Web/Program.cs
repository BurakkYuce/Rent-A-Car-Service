using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RentACar.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using RentACar.Application;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure;
using RentACar.Web.Observability;
using RentACar.Web.Calendar;
using RentACar.Infrastructure.Integrations;
using RentACar.Web.Identity;
using RentACar.Web.Platform;
using RentACar.Web.Bookings;
using RentACar.Web.Persistence;
using RentACar.Web.Reports;
using RentACar.Web.Documents;
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

// F13.1b: Blazor (AddRazorComponents + interaktif circuit) ve Radzen kaydı kalktı — arayüz /app (Angular SPA),
// veri /api/ui/v1.

// ---- Kültür: tr-TR (tarih dd.MM.yyyy, ondalık virgül, TRY) — PDF/export metinleri. FormParse explicit
// InvariantCulture kullanır → ETKİLENMEZ (bağımsız).
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
        // F13.1b: giriş ve yetkisiz hedefleri yeni arayüzde (/app anonim — challenge almaz, döngü yok). Asıl
        // karar aşağıdaki olaylarda (PermissionRedirect); bu iki değer yalnız çerçeve varsayılanı.
        options.LoginPath = RentACar.Web.Spa.Cutover.SpaLogin;
        options.AccessDeniedPath = RentACar.Web.Spa.Cutover.SpaPanel;
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
            if (RentACar.Web.Api.UiApiExtensions.UiPath(ctx.Request.Path))
                return RentACar.Web.Api.UiApiExtensions.WriteAsync(ctx.HttpContext,
                    RentACar.Web.Api.UiError.NoSession, "Oturum açık değil ya da süresi doldu.");
            ctx.Response.Redirect(PermissionRedirect.LoginRedirect(
                ctx.Request.Method, ctx.Request.Path, ctx.Request.QueryString,
                PermissionRedirect.SameOriginPath(ctx.Request.Headers.Referer, ctx.Request.Host)));
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (RentACar.Web.Api.UiApiExtensions.UiPath(ctx.Request.Path)) // F1.2: /yetkisiz HTML'i değil
                return RentACar.Web.Api.UiApiExtensions.WriteAsync(ctx.HttpContext,
                    RentACar.Web.Api.UiError.Forbidden, "Bu işlem için yetkiniz yok.");
            ctx.Response.Redirect(PermissionRedirect.UnauthorizedTarget(ctx.Request.Path));
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
var loginWindowSeconds = builder.Configuration.GetValue("RateLimit:LoginWindowSeconds", 60);
var clientErrorPermit = builder.Configuration.GetValue("RateLimit:IstemciHataPermit", 30);
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPermit,
            Window = TimeSpan.FromSeconds(loginWindowSeconds),
            QueueLimit = 0,
        }));
    // F3.3: yeni arayüzün istemci hata raporu (POST /api/ui/v1/istemci-hata) — IP başına; limiter kimlik
    // doğrulamadan ÖNCE koştuğu için kullanıcıya göre bölünemez. İstemci de sayfa başına 10 raporla sınırlı.
    o.AddPolicy(RentACar.Web.Api.IstemciHata.ClientErrorApi.RatePolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = clientErrorPermit,
            Window = TimeSpan.FromSeconds(60),
            QueueLimit = 0,
        }));
    // F11.1b güvenlik: dış çağrı yapan ayar eylemleri (SMTP/SMS/WhatsApp test gönderimi, alan adı ekle/doğrula) —
    // girişten AYRI kova (bu eylemler girişi kilitlemesin, giriş denemeleri de bunları tüketmesin). IP başına.
    var externalActionPermit = builder.Configuration.GetValue("RateLimit:ExternalActionPermit", 20);
    o.AddPolicy(RentACar.Web.Api.Sistem.SystemAdminApi.ExternalActionRatePolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = externalActionPermit,
            Window = TimeSpan.FromSeconds(60),
            QueueLimit = 0,
        }));
    // F13.1b: Blazor giriş formları kalktı; hız sınırı yalnız /api/ui uçlarında (giriş dahil) → 429 ProblemDetails
    // (SPA formu korur, bekletir). Başka yolda ham 429 (gövdesiz; yönlendirme yok).
    o.OnRejected = async (ctx, ct) =>
    {
        var policy = ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "login";
        RentACar.Application.Observability.RacarMetrics.RateLimitRejected(policy); // metrik: rate-limit reddi
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (RentACar.Web.Api.UiApiExtensions.UiPath(ctx.HttpContext.Request.Path))
            await RentACar.Web.Api.UiApiExtensions.WriteAsync(ctx.HttpContext, RentACar.Web.Api.UiError.TooManyRequests,
                policy == "login" ? "Çok fazla deneme; biraz sonra tekrar deneyin." : "Çok fazla istek; biraz sonra tekrar deneyin.");
    };
});

// F1.2: /api/ui/v1 — antiforgery başlık adı (X-XSRF-TOKEN) + OpenAPI (yalnız Development).
builder.Services.AddUiApi(builder.Environment);

// ITenantContext / ICurrentUser → HttpContextIdentity (claim'i her erişimde taze okur). F13.1b: Blazor circuit'i
// kalktığı için CircuitTenantContext/HybridIdentity köprüsü gereksiz; her istek gerçek HTTP isteği. Alt katman
// (interceptor/RLS/factory) değişmez.
builder.Services.AddScoped<HttpContextIdentity>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpContextIdentity>());
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<HttpContextIdentity>());

// ---- Platform süper-admin (tenant'tan bağımsız operatör konsolu) ----
// Kimlik config'ten; ÜRETİMDE ZORUNLU (Pii:HmacKey deseni — yoksa açılış reddeder, arka kapı yok).
var platformUser = builder.Configuration["Platform:AdminUser"];
var platformHash = builder.Configuration["Platform:AdminPasswordHash"];
string? platformDevPassword = null; // Development'ta hash yoksa bu açılışa özel üretilir (repoda sabit parola YOK)
if (builder.Environment.IsDevelopment())
{
    platformUser ??= "admin";
    if (string.IsNullOrWhiteSpace(platformHash))
    {
        platformDevPassword = DevelopmentPassword.Generate();
        platformHash = PlatformCredentials.HashPassword(platformDevPassword);
    }
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
builder.Services.AddScoped<RentACar.Web.Common.ValidationErrorMiddleware>(); // ValidationException → 500 DEĞİL, kullanıcıya gösterilebilir hata
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
builder.Services.AddScoped<RentACar.Web.Bookings.ContractViewService>();
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
builder.Services.AddHostedService<RentACar.Web.Jobs.DueNotificationJob>(); // scheduler: vade→bildirim (kimliksiz)
builder.Services.AddHostedService<RentACar.Web.Jobs.PeriodInvoiceJob>(); // FAZ 4.2-B4: dönemsel fatura job'ı (ayar-kapılı)
builder.Services.AddHttpClient(); // TCMB kur çekimi
builder.Services.AddSingleton<RentACar.Web.Kur.TcmbExchangeRateService>(); // TCMB kur çek/upsert (paylaşımlı KurKayitlari)
builder.Services.AddHostedService<RentACar.Web.Jobs.TcmbExchangeRateJob>(); // scheduler: TCMB günlük kur (kimliksiz)
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

if (platformDevPassword is not null)
    app.Logger.LogWarning(
        "Platform operatörü parolası (Development, yalnız bu açılış): {Kullanici:l} / {Parola:l} — sabitlemek için "
        + "Platform:AdminPasswordHash (bkz. --platform-hash)", platformUser, platformDevPassword);

// ---- Şema + seed (owner bağlantısı) ----
await DbInitializer.MigrateAndSeedAsync(app.Services, migratorConn);

// ---- Pipeline ----
// F13.1b: Blazor /Error ve /not-found sayfaları kalktı. Tarayıcı gezinmesindeki işlenmemiş istisna ve gövdesiz 404
// yeni arayüzün Panel'ine hata bandıyla (?hata=) gider; karar saf fonksiyonda (Cutover.StatusTarget — /api/ui,
// /app, uzantılı dosya istekleri ve GET dışı yöntemler HAM durum kodunu alır). Destek kodu loglardaki request_id
// (trace id) ile aynı ifade.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(new ExceptionHandlerOptions
    {
        ExceptionHandler = ctx =>
        {
            var code = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
            if (RentACar.Web.Spa.Cutover.StatusTarget(ctx.Request.Method, ctx.Request.Path, StatusCodes.Status500InternalServerError, code) is { } target)
                ctx.Response.Redirect(target);
            else
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        },
    });
    app.UseHsts();
}
app.UseStatusCodePages(async context =>
{
    var http = context.HttpContext;
    if (RentACar.Web.Spa.Cutover.StatusTarget(http.Request.Method, http.Request.Path, http.Response.StatusCode) is { } target)
        http.Response.Redirect(target);
    // Müşteri bağlantısı (/sozlesme/{token}, /feed): geçersiz/iptal → personel arayüzü DEĞİL, sade metin (aynı 404).
    else if (http.Response.StatusCode == StatusCodes.Status404NotFound && HttpMethods.IsGet(http.Request.Method)
             && RentACar.Web.Spa.Cutover.IsPublicLinkPath(http.Request.Path))
    {
        http.Response.ContentType = "text/plain; charset=utf-8";
        await http.Response.WriteAsync(RentACar.Web.Spa.Cutover.PublicLinkNotFoundText);
    }
});
// F1.2: /api/ui için no-store + StatusCodePages/istisna HTML'i yerine ProblemDetails (ikisinin İÇİNDE durmalı).
app.UseUiApiPipeline();

app.UseRequestLocalization(); // tr-TR (yukarıda Configure edildi) — PDF/export tarih/sayı biçimi tutarlı

// Reverse-proxy (Caddy/nginx aynı makinede) arkasında gerçek istemci IP'si — rate limit doğru IP'yi görsün.
// Varsayılan KnownProxies=loopback: uzak istemciden gelen sahte X-Forwarded-For'a güvenilmez.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

// ---- Güvenlik yanıt başlıkları (defense-in-depth: Caddy'den bağımsız, HER yanıtta) ----
// CSP script-src 'self' (inline event handler'lar harici JS'e taşındı → rc-ui.js). style-src 'unsafe-inline'
// bilinçli pragmatik: inline style attr'ları/blokları kalıyor (XSS riski script'e göre düşük; tümünü ayıklamak
// devasa iş). connect-src 'self' → SPA yalnız kendi kökenindeki /api/ui'ye bağlanır. frame-ancestors
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
        // script-src 'self' — HİÇBİR inline script yok (SPA derlemesi harici dosyalar; F13.1b'de Blazor kabuğu ve
        // rc-*.js betikleri kalktı). 'unsafe-inline' YOK, hash YOK.
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
app.UseMiddleware<RentACar.Web.Common.ValidationErrorMiddleware>();
app.UseMiddleware<RentACar.Web.Observability.RequestEnrichment.Middleware>();
// Anlık kesme: kapatılan tenant'ın authenticated isteği (açık oturum) bir sonraki istekte /login'e düşer.
app.UseMiddleware<TenantActiveMiddleware>();
app.UseMiddleware<PlatformIsolationMiddleware>(); // platform operatörü tenant UI'ına giremez (konsola yönlendir)
// Kesiş: GET /login → /app/giris (tek giriş); eski Blazor sayfa adresleri → /app (301, herkes; açık şablon listesi —
// PDF/export yönlenmez); eski kabuk sayfaları → Panel + hata bandı. Kapalı firma ve platform ayrımı ÖNCE çalışsın
// diye onlardan sonra.
app.UseMiddleware<RentACar.Web.Spa.CutoverMiddleware>();
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

// F1.5 — yeni arayüz kabuğu /app altında ANONİM (cookie challenge yok → /login döngüsü yok); Spa:Dizin
// content root'a göreli (varsayılan ../app/browser). Güvenlik başlıkları/CSP yukarıdaki genel middleware'den.
RentACar.Web.Spa.SpaHosting.MapSpaHosting(app);
// F13 sonrası: eski form çıkış ucu (POST /auth/logout) kaldırıldı — üretimde CSRF korumasızdı (minimal API form
// bağlamayan uca antiforgery doğrulaması eklemez). Giriş/çıkış yalnız /api/ui/v1/oturum/* (X-XSRF-TOKEN zorunlu).
app.MapUiApi();                   // F1.2 — /api/ui/v1 (yeni arayüz JSON katmanı: CSRF + pilot kapısı + ProblemDetails)
// F13.1a: Blazor form POST uçları (~330) ve yalnız Blazor'un kullandığı GET'ler silindi — yazma yolu yalnız
// /api/ui/v1 (ve harici /api/v1, ayrı proje). Kalan GET'ler yeni arayüzün bağlandığı dosya/indirme uçları.
app.MapCalendarFeedEndpoints();   // kimliksiz iCal feed
app.MapReportExportEndpoints();   // GET /raporlar/export/{rapor} (rapor ekranı dışa aktarım)
app.MapListExportEndpoints();     // GET /listeler/export… (liste dışa aktarım)
app.MapPdfEndpoints();            // GET sözleşme/fatura PDF
app.MapCompanyDocumentEndpoints(); // GET /firma-belgeleri/{id}/indir (DocumentApi indirme adresi)
app.MapCompanyFileEndpoints();    // GET /dokumanlar/{id}/indir (DocumentApi indirme adresi)
app.MapContractViewEndpoints();   // GET /sozlesme/{token} ANONİM (ERP host'unda, PublicSite'ta değil)

app.Run();
