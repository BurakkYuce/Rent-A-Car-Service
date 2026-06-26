using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
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
        return services;
    }
}
