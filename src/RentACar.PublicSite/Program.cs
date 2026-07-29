using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using RentACar.Application;
using RentACar.Domain.Common;
using RentACar.Infrastructure;
using RentACar.PublicSite;
using RentACar.PublicSite.Components;

var builder = WebApplication.CreateBuilder(args);

// ---- Bağlantı: yalnız Default (racar_app, RLS uygulanan runtime) — bu proje migration ÇALIŞTIRMAZ. ----
var appConn = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");

// ---- Blazor: static SSR (interaktif circuit YOK — halka açık siteye ziyaretçi başına
// SignalR circuit/bellek maliyeti bindirmemek bilinçli bir tercih; formlar minimal-API'ye POST eder). ----
builder.Services.AddRazorComponents();

// ---- İstek-scoped tenant bağlamı (host-çözümleme middleware'i doldurur). ----
builder.Services.AddScoped<PublicTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<PublicTenantContext>());
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<PublicTenantContext>());

// ---- PR-2/PR-3.5: host→tenant çözümleme (Found değilse 404 — asla varsayılan tenant'a düşmez).
// Singleton ŞART: CachedPublicTenantResolver kendi MemoryCache'ini istekler arası TAŞIMALI — Scoped
// olsaydı her istekte sıfırdan cache kurulur, cache hiç işe yaramazdı. PublicTenantResolver da
// stateless (DI-dışı ham context kurar) — Singleton'a aday, scoped'a gerek yok. AddMemoryCache()
// GEREKMEZ — CachedPublicTenantResolver kendi dedike MemoryCache'ini kendi içinde `new`'ler
// (paylaşımlı IMemoryCache'e SizeLimit koymanın diğer tüketicileri etkileme riski var — bkz. sınıfın kendi yorumu). ----
builder.Services.AddSingleton<PublicTenantResolver>();
builder.Services.AddSingleton<IPublicTenantResolver>(sp =>
    new CachedPublicTenantResolver(sp.GetRequiredService<PublicTenantResolver>()));
builder.Services.AddScoped<TenantHostResolutionMiddleware>();

// ---- PR-5: Caddy on_demand_tls ask-endpoint cache'i — CachedPublicTenantResolver'ın AYNI Singleton +
// dedike-MemoryCache deseni (bkz. DomainAskCache.cs). ----
builder.Services.AddSingleton<DomainAskCache>();

// PII blind-index anahtarı — bu proje v1'de PII şifreli alan okumaz/yazmaz, ama Infrastructure paylaşımlı
// katman olduğu için Web/Api ile AYNI üretim guard'ı (dev dışı ortamda zorunlu) tutarlılık için uygulanır.
var piiKey = builder.Configuration["Pii:HmacKey"];
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(piiKey))
    throw new InvalidOperationException("Pii:HmacKey bu ortamda zorunludur (PII blind-index anahtarı).");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(appConn, piiKey);

// ---- PR-8: rate-limit (IP başına sabit pencere). PR-4.5'in UseForwardedHeaders'ına BAĞIMLI —
// o olmadan Caddy arkasında RemoteIpAddress hep 127.0.0.1 olur, limit tek partition'a düşerdi.
// İKİ policy: sıkı "booking-request" (talep POST'u — spam/lead-flood) + gevşek "public-search"
// (/musaitlik: her istek availability sorgusu + grup başına quote motoru → en pahalı anonim uç). ----
var talepPermit = builder.Configuration.GetValue("RateLimit:BookingRequestPermit", 5);
var talepWindowSec = builder.Configuration.GetValue("RateLimit:BookingRequestWindowSeconds", 60);
var aramaPermit = builder.Configuration.GetValue("RateLimit:PublicSearchPermit", 30);
var aramaWindowSec = builder.Configuration.GetValue("RateLimit:PublicSearchWindowSeconds", 60);
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("booking-request", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        { PermitLimit = talepPermit, Window = TimeSpan.FromSeconds(talepWindowSec), QueueLimit = 0 }));

    o.AddPolicy("public-search", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        { PermitLimit = aramaPermit, Window = TimeSpan.FromSeconds(aramaWindowSec), QueueLimit = 0 }));

    // Static-SSR akışı: ham 429 gövdesi yerine anlamlı sayfaya yönlendir (Web'in login deseniyle tutarlı).
    o.OnRejected = (ctx, _) =>
    {
        RentACar.Application.Observability.RacarMetrics.RateLimitRejected("public-site");
        ctx.HttpContext.Response.Redirect("/cok-istek");
        return ValueTask.CompletedTask;
    };
});

builder.Services.AddHealthChecks();

var app = builder.Build();

// Reverse-proxy (Caddy, aynı makinede) arkasında gerçek istemci IP'si — roadmap PR-8'in rate-limit'i
// doğru IP'yi görsün diye ŞİMDİDEN kurulur (PR-4.5). Varsayılan KnownProxies=loopback: uzak istemciden
// gelen sahte X-Forwarded-For'a güvenilmez — Web/Api ile AYNI desen (Program.cs'lerinde doğrulandı).
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseRateLimiter(); // PR-8 — ForwardedHeaders'tan SONRA (gerçek IP partition'ı)

app.UseStaticFiles();
app.UseAntiforgery();
app.UseMiddleware<TenantHostResolutionMiddleware>();

app.MapHealthChecks("/health/live");

app.MapVehiclePhotoEndpoints(); // PR-4
app.MapDomainVerificationEndpoints(); // PR-5: Caddy on_demand_tls ask
app.MapBlogEndpoints(); // PR-6: blog kapak serve
app.MapPublicBookingRequestEndpoints(); // PR-8: talep formu POST

app.MapRazorComponents<App>();

app.Run();
