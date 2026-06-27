using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Availability;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.DamageFiles;
using RentACar.Application.Details;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Hgs;
using RentACar.Application.Penalties;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
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
        services.AddScoped<ReservationService>();
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
        return services;
    }
}
