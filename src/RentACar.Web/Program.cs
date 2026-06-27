using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using RentACar.Application;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure;
using RentACar.Web.Components;
using RentACar.Web.Identity;
using RentACar.Web.Bookings;
using RentACar.Web.Branches;
using RentACar.Web.Customers;
using RentACar.Web.DamageFiles;
using RentACar.Web.Expenses;
using RentACar.Web.Finance;
using RentACar.Web.Penalties;
using RentACar.Web.Persistence;
using RentACar.Web.Regulation;
using RentACar.Web.ServiceRecords;
using RentACar.Web.Users;
using RentACar.Web.VehicleSales;
using RentACar.Web.Vehicles;

var builder = WebApplication.CreateBuilder(args);

// ---- Bağlantılar: Default = racar_app (RLS uygulanan runtime), Migrator = racar_owner (DDL/seed) ----
var appConn = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
var migratorConn = builder.Configuration.GetConnectionString("Migrator")
    ?? throw new InvalidOperationException("ConnectionStrings:Migrator eksik.");

// ---- Blazor (static SSR + interaktif sunucu bileşenleri kayıtlı; araç ekranları SSR) ----
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ---- Kimlik / yetki (cookie, 2 aşamalı login) ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, SsrAuthenticationStateProvider>();

// ITenantContext / ICurrentUser → HttpContext claim'lerinden (tek örnek iki arayüze).
builder.Services.AddScoped<HttpContextIdentity>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpContextIdentity>());
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<HttpContextIdentity>());

// ---- Uygulama + altyapı ----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(appConn);

// Login servisi + şifre hash.
builder.Services.AddScoped<LoginService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
// Application IPasswordHasher → ASP.NET PasswordHasher köprüsü (kullanıcı yönetimi).
builder.Services.AddSingleton<RentACar.Application.Common.IPasswordHasher, RentACar.Web.Identity.AspNetPasswordHasher>();

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

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAuthEndpoints();
app.MapVehicleEndpoints();
app.MapCustomerEndpoints();
app.MapBookingEndpoints();
app.MapFinanceEndpoints();
app.MapExpenseEndpoints();
app.MapRegulationEndpoints();
app.MapPenaltyEndpoints();
app.MapVehicleSaleEndpoints();
app.MapDamageFileEndpoints();
app.MapServiceRecordEndpoints();
app.MapUserEndpoints();
app.MapBranchEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
