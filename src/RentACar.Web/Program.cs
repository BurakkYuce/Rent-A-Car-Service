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
using RentACar.Web.Reports;
using RentACar.Web.ServiceRecords;
using RentACar.Web.TenantSettings;
using RentACar.Web.Personnel;
using RentACar.Web.Legal;
using RentACar.Web.Crm;
using RentACar.Web.Periods;
using RentACar.Web.Authorization;
using RentACar.Web.Users;
using RentACar.Web.VehicleGroups;
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

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// roadmap E2: antiforgery yalnız PROD'da zorunlu (dev/test gevşek). Map'lerden ÖNCE set edilir
// (AntiforgeryByEnv build-time okur). Formlar <AntiforgeryToken/> taşır → prod'da CSRF korumalı.
RentACar.Web.Identity.FormSecurity.EnforceAntiforgery = app.Environment.IsProduction();

app.MapStaticAssets();
app.MapAuthEndpoints();
app.MapVehicleEndpoints();
app.MapCustomerEndpoints();
app.MapBookingEndpoints();
app.MapRentalAddOnEndpoints();
app.MapQuotationEndpoints();
app.MapFinanceEndpoints();
app.MapExpenseEndpoints();
app.MapRegulationEndpoints();
app.MapPenaltyEndpoints();
app.MapVehicleSaleEndpoints();
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
app.MapRateMatrixEndpoints();
app.MapCoverageProductEndpoints();
app.MapRentalRuleEndpoints();
app.MapQuoteEndpoints();
app.MapEkHizmetEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
