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

// ---- PR-2: host→tenant çözümleme (Found değilse 404 — asla varsayılan tenant'a düşmez). ----
builder.Services.AddScoped<PublicTenantResolver>();
builder.Services.AddScoped<TenantHostResolutionMiddleware>();

// PII blind-index anahtarı — bu proje v1'de PII şifreli alan okumaz/yazmaz, ama Infrastructure paylaşımlı
// katman olduğu için Web/Api ile AYNI üretim guard'ı (dev dışı ortamda zorunlu) tutarlılık için uygulanır.
var piiKey = builder.Configuration["Pii:HmacKey"];
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(piiKey))
    throw new InvalidOperationException("Pii:HmacKey bu ortamda zorunludur (PII blind-index anahtarı).");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(appConn, piiKey);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();
app.UseMiddleware<TenantHostResolutionMiddleware>();

app.MapHealthChecks("/health/live");

app.MapRazorComponents<App>();

app.Run();
