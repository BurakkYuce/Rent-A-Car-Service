using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Auditing;
using RentACar.Application.Availability;
using RentACar.Application.Bookings;
using RentACar.Application.Branches;
using RentACar.Application.CancelReasons;
using RentACar.Application.Brands;
using RentACar.Application.Currencies;
using RentACar.Application.Customers;
using RentACar.Application.DamageFiles;
using RentACar.Application.Details;
using RentACar.Application.EkHizmetler;
using RentACar.Application.Expenses;
using RentACar.Application.KdvRates;
using RentACar.Application.Finance;
using RentACar.Application.Fleet;
using RentACar.Application.Hgs;
using RentACar.Application.Locations;
using RentACar.Application.Penalties;
using RentACar.Application.PenaltyTypes;
using RentACar.Application.Pricing;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.ReservationSources;
using RentACar.Application.CoverageProducts;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Users;
using RentACar.Application.VehicleGroups;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;

namespace RentACar.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<VehicleService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<BranchService>();
        services.AddScoped<RateCardService>();
        services.AddScoped<PricingService>();
        services.AddScoped<RentalQuoteEngine>();
        services.AddScoped<LocationService>();
        services.AddScoped<EkHizmetTanimService>();
        services.AddScoped<RentACar.Application.FuelKinds.FuelKindService>();
        services.AddScoped<RentACar.Application.TransmissionTypes.TransmissionTypeService>();
        services.AddScoped<RentACar.Application.VehicleColors.VehicleColorService>();
        services.AddScoped<RentACar.Application.CustomerGroups.CustomerGroupService>();
        services.AddScoped<RentACar.Application.InsuranceCompanies.InsuranceCompanyService>();
        services.AddScoped<RentACar.Application.Banks.BankService>();
        services.AddScoped<RentACar.Application.Departments.DepartmentService>();
        services.AddScoped<RentACar.Application.PaymentTypes.PaymentTypeService>();
        services.AddScoped<RentACar.Application.Countries.CountryService>();
        services.AddScoped<RentACar.Application.Accessories.AccessoryService>();
        services.AddScoped<CancelReasonService>();
        services.AddScoped<ReservationSourceService>();
        services.AddScoped<RentACar.Application.VehicleSegments.VehicleSegmentService>();
        services.AddScoped<RentACar.Application.VehicleTypes.VehicleTypeService>();
        services.AddScoped<RentACar.Application.VehicleOwners.VehicleOwnerService>();
        services.AddScoped<RentACar.Application.ExpenseCategories.ExpenseCategoryService>();
        services.AddScoped<RentACar.Application.FinancialAccounts.FinancialAccountService>();
        services.AddScoped<RentACar.Application.CustomCodes.CustomCodeService>();
        services.AddScoped<BrandService>();
        services.AddScoped<CurrencyService>();
        services.AddScoped<KdvRateService>();
        services.AddScoped<VehicleGroupService>();
        services.AddScoped<RateMatrixService>();
        services.AddScoped<CoverageProductService>();
        services.AddScoped<RentalRuleService>();
        services.AddScoped<TenantSettings.TenantSettingsService>();
        services.AddScoped<Personnel.PersonelService>();
        services.AddScoped<Legal.HukukDosyaService>();
        services.AddScoped<Crm.AnketService>();
        services.AddScoped<Crm.SikayetService>();
        services.AddScoped<Search.SearchService>();
        services.AddScoped<Periods.DonemKilidiService>();
        services.AddScoped<Periods.IPeriodLockGuard>(sp => sp.GetRequiredService<Periods.DonemKilidiService>());
        services.AddScoped<Dashboard.DashboardService>();
        services.AddScoped<Authorization.ScreenPermissionService>();
        services.AddScoped<FleetStatusService>();
        services.AddScoped<ReservationService>();
        services.AddScoped<QuotationService>();
        services.AddScoped<CalendarService>();
        services.AddScoped<RentalService>();
        services.AddScoped<RentACar.Application.RentalAddOns.RentalAddOnService>();
        services.AddScoped<CashService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<ExpenseService>();
        services.AddScoped<RegulationService>();
        services.AddScoped<VadeService>();
        services.AddScoped<Notifications.BildirimService>(); // roadmap G6: bildirim merkezi agrega
        services.AddScoped<PenaltyService>();
        services.AddScoped<PenaltyTypeService>();
        services.AddScoped<HgsReflectionService>();
        services.AddScoped<VehicleSaleService>();
        services.AddScoped<FiloKiralamalar.FiloKiralamaService>(); // roadmap L1
        services.AddScoped<DamageFileService>();
        services.AddScoped<ServiceRecordService>();
        services.AddScoped<ReportService>();
        services.AddScoped<UserService>();
        services.AddScoped<AvailabilityService>();
        services.AddScoped<DetailService>();
        services.AddScoped<AuditService>();
        return services;
    }
}
