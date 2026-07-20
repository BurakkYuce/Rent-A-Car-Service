using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RentACar.Api.Common;
using RentACar.Api.Endpoints;
using RentACar.Api.Identity;
using RentACar.Application;
using RentACar.Domain.Common;
using RentACar.Infrastructure;
using RentACar.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// ---- Gözlemlenebilirlik (P0-2): yapılandırılmış log — konsol + günlük dönen dosya (14 gün saklama) ----
builder.Services.AddSerilog(lc => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    // Dosya sink JSON: tenant_id/user/request_id birinci-sınıf alan (ileride ingest kolay).
    .WriteTo.File(new Serilog.Formatting.Compact.CompactJsonFormatter(),
        builder.Configuration["Logging:FilePath"] ?? "logs/rentacar-api-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

var appConn = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");

// JSON: enum'lar string olarak (durum/yakıt vb.).
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Uygulama + altyapı (LoginService + IPasswordHasher<User> AddInfrastructure'da kayıtlı).
// PII blind-index anahtarı (KVKK/F2): Development DIŞINDA her ortamda ZORUNLU (Staging dahil —
// adversarial M1: gerçek PII'lı ortam bilinen dev anahtarına sessizce düşmemeli).
var piiKey = builder.Configuration["Pii:HmacKey"];
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(piiKey))
    throw new InvalidOperationException("Pii:HmacKey bu ortamda zorunludur (PII blind-index anahtarı).");
// DataProtection key-ring kalıcı dizini (adversarial M1) — bkz. Web/Program.cs: geçici FS'te PII kalıcı çözülemez.
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RACAR_DP_KEYS")))
    throw new InvalidOperationException("RACAR_DP_KEYS bu ortamda zorunludur (DataProtection key-ring kalıcı dizini; yoksa redeploy'da PII çözülemez).");
builder.Services.AddApplication();
builder.Services.AddInfrastructure(appConn, piiKey);

// Kimlik: ITenantContext + ICurrentUser → JWT bearer claim'lerinden (tek örnek iki arayüze).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ApiIdentity>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ApiIdentity>());
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<ApiIdentity>());

// Anlık kesme (erişim aç/kapa): tenant durumunu kısa-ömürlü cache'le (Web ile ORTAK TenantStatusCache).
builder.Services.AddMemoryCache();
builder.Services.AddScoped<TenantStatusCache>();

// JWT imzalama anahtarı (adversarial C1 — pre-launch): Pii:HmacKey deseniyle üretimde ZORUNLU. Committed
// appsettings'te anahtar YOK (public repoda sabit anahtar = herkes Admin token forge eder → tam bypass + PII).
// Boş/dev/zayıf anahtar prod'a SIZAMAZ; yalnız Development'ta sabit fallback.
const string devJwtKey = "dev-only-symmetric-key-change-in-production-min-32-bytes!!";
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Jwt:Key bu ortamda zorunludur (API token imzalama; min 32 bayt).");
    jwtKey = devJwtKey;
}
else if (!builder.Environment.IsDevelopment() && (jwtKey.Length < 32 || jwtKey == devJwtKey))
    throw new InvalidOperationException("Jwt:Key üretimde dev/zayıf anahtar olamaz (min 32 bayt, özgün).");
builder.Configuration["Jwt:Key"] = jwtKey; // JwtTokenService (IOptions) + doğrulama aynı çözülen anahtarı görsün

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<JwtTokenService>();
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = signingKey,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents
        {
            // Anlık kesme: token GEÇERLİ olsa da tenant KAPALIYSA isteği reddet (401). Platform konsolu
            // Web'de toggle'lar; API ayrı process olduğundan cache ~60sn TTL ile bayat olabilir (kabul —
            // launch-time IsActive + ≤60sn API-kesme). Tenants platform tablosu → cache app-conn ile okur.
            OnTokenValidated = async ctx =>
            {
                var tid = ctx.Principal?.FindFirst(ApiClaims.TenantId)?.Value;
                if (Guid.TryParse(tid, out var tenantId))
                {
                    var cache = ctx.HttpContext.RequestServices.GetRequiredService<TenantStatusCache>();
                    if (!await cache.IsActiveAsync(tenantId, ctx.HttpContext.RequestAborted))
                        ctx.Fail("Tenant erişimi kapatıldı.");
                }
            },
        };
    });
builder.Services.AddAuthorization();

// ---- Login brute-force koruması (P0): IP başına sabit-pencere limiti (yalnız "login" policy'li uçlar) ----
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
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.Headers.RetryAfter = loginWindowSec.ToString();
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new ApiError("too_many_requests", "Çok fazla giriş denemesi. Lütfen bekleyip yeniden deneyin."), ct);
    };
});

builder.Services.AddOpenApi();

builder.Services.AddScoped<RentACar.Api.Observability.ApiRequestEnrichment.Middleware>();
builder.Services.AddHealthChecks()
    .AddCheck<RentACar.Api.Observability.ApiDbHealthCheck>("db", tags: ["ready"]);
RentACar.Api.Observability.ApiObservabilitySetup.AddRacarObservability(builder.Services, builder.Configuration, "rentacar-api");

var app = builder.Build();

// Reverse-proxy (Caddy/nginx aynı makinede) arkasında gerçek istemci IP'si — rate limit doğru IP'yi görsün.
// Varsayılan KnownProxies=loopback: uzak istemciden gelen sahte X-Forwarded-For'a güvenilmez.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
app.UseSerilogRequestLogging(o => // istek başına tek satır + tenant/user/req-id korelasyonu
    o.EnrichDiagnosticContext = RentACar.Api.Observability.ApiRequestEnrichment.Enrich);
app.UseRateLimiter();

// Tutarlı JSON hata zarfı (en dış katman).
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.MapOpenApi(); // OpenAPI dokümanı: /openapi/v1.json

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RentACar.Api.Observability.ApiRequestEnrichment.Middleware>(); // log: tenant/user/req-id

app.MapAuthApi();
app.MapVehiclesApi();
app.MapCustomersApi();
app.MapReservationsApi();
app.MapRentalsApi();
app.MapReportsApi();
app.MapFinanceApi();
app.MapEkHizmetlerApi();
app.MapModulesApi();

// Sağlık — liveness/readiness (MS deseni). Anonim (ops ping'i).
var apiReady = new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready"),
    ResponseWriter = RentACar.Api.Observability.ApiHealthResponse.Write, // {status:healthy/unhealthy} sözleşmesi
};
app.MapHealthChecks("/health/live", new()
{
    Predicate = _ => false,
    ResponseWriter = RentACar.Api.Observability.ApiHealthResponse.Write,
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", apiReady).AllowAnonymous();
app.MapHealthChecks("/health", apiReady).AllowAnonymous(); // geriye-uyum

app.Run();
