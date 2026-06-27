using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.DamageFiles;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Penalties;
using RentACar.Application.Regulation;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Infrastructure.Integrations;
using RentACar.Infrastructure.Persistence;
using RentACar.Infrastructure.Persistence.Interceptors;
using RentACar.Infrastructure.Persistence.Repositories;

namespace RentACar.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Infrastructure servislerini kaydeder. ITenantContext ve ICurrentUser impl'leri
    /// ÇAĞIRAN (Web/Tests) tarafından scoped kaydedilmelidir.
    /// </summary>
    /// <param name="appConnectionString">Runtime (racar_app — kısıtlı, RLS uygulanan) bağlantısı.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string appConnectionString)
    {
        // Interceptor'lar: bağlantı-tenant (scoped, ITenantContext okur) + audit (singleton).
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddSingleton<AuditSaveChangesInterceptor>();

        // DbContextOptions scope başına kurulur; interceptor'lar oradan eklenir.
        services.AddScoped(sp =>
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>();
            builder.UseNpgsql(appConnectionString);
            builder.AddInterceptors(
                sp.GetRequiredService<TenantConnectionInterceptor>(),
                sp.GetRequiredService<AuditSaveChangesInterceptor>());
            return builder.Options;
        });

        // Açık scoped factory (tenant/user'ı oluşturulan context'e taşır).
        services.AddScoped<IDbContextFactory<AppDbContext>, ScopedAppDbContextFactory>();

        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<ICashRepository, CashRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IRegulationRepository, RegulationRepository>();
        services.AddScoped<IPenaltyRepository, PenaltyRepository>();
        services.AddScoped<ILedgerPoster, LedgerPoster>();
        services.AddScoped<IVehicleSaleRepository, VehicleSaleRepository>();
        services.AddScoped<IDamageFileRepository, DamageFileRepository>();

        // Entegrasyon adapter'ları (v1 stub; gerçek impl Faz 2/3'te).
        services.AddIntegrationStubs();

        return services;
    }
}
