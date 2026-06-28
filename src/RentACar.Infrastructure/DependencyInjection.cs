using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Auditing;
using RentACar.Application.Availability;
using RentACar.Application.Bookings;
using RentACar.Application.Branches;
using RentACar.Application.Customers;
using RentACar.Application.DamageFiles;
using RentACar.Application.Details;
using RentACar.Application.EkHizmetler;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Fleet;
using RentACar.Application.Locations;
using RentACar.Application.Penalties;
using RentACar.Application.Pricing;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Users;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Identity;
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
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IRateCardRepository, RateCardRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IEkHizmetTanimRepository, EkHizmetTanimRepository>();
        services.AddScoped<IFleetStatusRepository, FleetStatusRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IQuotationRepository, QuotationRepository>();
        services.AddScoped<ICalendarRepository, CalendarRepository>();
        services.AddScoped<ICashRepository, CashRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IRegulationRepository, RegulationRepository>();
        services.AddScoped<IPenaltyRepository, PenaltyRepository>();
        services.AddScoped<ILedgerPoster, LedgerPoster>();
        services.AddScoped<IVehicleSaleRepository, VehicleSaleRepository>();
        services.AddScoped<IDamageFileRepository, DamageFileRepository>();
        services.AddScoped<IServiceRecordRepository, ServiceRecordRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAvailabilityRepository, AvailabilityRepository>();
        services.AddScoped<IDetailRepository, DetailRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();

        // Kimlik/şifre: paylaşılan login doğrulaması (Web cookie + API JWT ortak kullanır).
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<RentACar.Application.Common.IPasswordHasher, AspNetPasswordHasher>();
        services.AddScoped<LoginService>();

        // Entegrasyon adapter'ları (v1 stub; gerçek impl Faz 2/3'te).
        services.AddIntegrationStubs();

        return services;
    }
}
