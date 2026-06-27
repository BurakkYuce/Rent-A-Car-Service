using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Regulation;
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
        return services;
    }
}
