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
    .WriteTo.File(
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
builder.Services.AddApplication();
builder.Services.AddInfrastructure(appConn, piiKey);

// Kimlik: ITenantContext + ICurrentUser → JWT bearer claim'lerinden (tek örnek iki arayüze).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ApiIdentity>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ApiIdentity>());
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<ApiIdentity>());

// JWT üretimi + doğrulama.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<JwtTokenService>();
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key));

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

var app = builder.Build();

// Reverse-proxy (Caddy/nginx aynı makinede) arkasında gerçek istemci IP'si — rate limit doğru IP'yi görsün.
// Varsayılan KnownProxies=loopback: uzak istemciden gelen sahte X-Forwarded-For'a güvenilmez.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
app.UseSerilogRequestLogging(); // istek başına tek satır: metot, yol, durum, süre
app.UseRateLimiter();

// Tutarlı JSON hata zarfı (en dış katman).
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.MapOpenApi(); // OpenAPI dokümanı: /openapi/v1.json

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthApi();
app.MapVehiclesApi();
app.MapCustomersApi();
app.MapReservationsApi();
app.MapRentalsApi();
app.MapReportsApi();
app.MapFinanceApi();
app.MapEkHizmetlerApi();
app.MapModulesApi();

// Sağlık (readiness): DB bağlanabiliyor mu? Anonim (ops ping'i).
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
}).AllowAnonymous().WithTags("Health");

app.Run();
