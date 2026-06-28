using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Auditing;
using RentACar.Application.Availability;
using RentACar.Application.Bookings;
using RentACar.Application.Branches;
using RentACar.Application.CancelReasons;
using RentACar.Application.Customers;
using RentACar.Application.DamageFiles;
using RentACar.Application.Details;
using RentACar.Application.EkHizmetler;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Hgs;
using RentACar.Application.Locations;
using RentACar.Application.Penalties;
using RentACar.Application.Pricing;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.ReservationSources;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Users;
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
        services.AddScoped<LocationService>();
        services.AddScoped<EkHizmetTanimService>();
        services.AddScoped<RentACar.Application.InsuranceCompanies.InsuranceCompanyService>();
        services.AddScoped<CancelReasonService>();
        services.AddScoped<ReservationSourceService>();
        services.AddScoped<RentACar.Application.VehicleSegments.VehicleSegmentService>();
        services.AddScoped<RentACar.Application.VehicleTypes.VehicleTypeService>();
        services.AddScoped<RentACar.Application.VehicleOwners.VehicleOwnerService>();
        services.AddScoped<RentACar.Application.ExpenseCategories.ExpenseCategoryService>();
        services.AddScoped<RentACar.Application.FinancialAccounts.FinancialAccountService>();
        services.AddScoped<RentACar.Application.CustomCodes.CustomCodeService>();
        services.AddScoped<ReservationService>();
        services.AddScoped<QuotationService>();
        services.AddScoped<CalendarService>();
        services.AddScoped<RentalService>();
        services.AddScoped<CashService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<ExpenseService>();
        services.AddScoped<RegulationService>();
        services.AddScoped<VadeService>();
        services.AddScoped<PenaltyService>();
        services.AddScoped<HgsReflectionService>();
        services.AddScoped<VehicleSaleService>();
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
