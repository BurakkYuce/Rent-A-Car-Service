using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    /// <param name="piiHmacKey">PII blind-index HMAC anahtarı (Pii:HmacKey). null → dev anahtarı
    /// (üretim guard'ı Web/Api Program.cs'te).</param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string appConnectionString, string? piiHmacKey = null)
    {
        // Interceptor'lar: bağlantı-tenant (scoped, ITenantContext okur) + şube-FK çözücü + audit (singleton).
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddSingleton<BranchFkInterceptor>();
        services.AddSingleton<OfficeBranchInterceptor>(); // FAZ 5-C4
        services.AddSingleton<AuditSaveChangesInterceptor>();
        services.AddSingleton<LedgerAmountGuardInterceptor>(); // F8.1a M1: defter baz tutarı son savunma

        // Şifreleme (roadmap D1): hassas tenant kimliklerini at-rest şifrele. Key-ring KALICI
        // (PersistKeysToFileSystem + SetApplicationName) → restart/redeploy sonrası aynı anahtarla çözülür.
        // Yol çözümü KeyRingPath'te (test edilebilir): fallback ARTIK bin/ altında DEĞİL — build çıktısına
        // yazmak `dotnet clean`/redeploy'da key-ring'i siliyor ve *Enc PII kalıcı çözülemez hale geliyordu.
        var keysDir = RentACar.Infrastructure.Security.KeyRingPath.Resolve();
        Directory.CreateDirectory(keysDir);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
            .SetApplicationName("RentACar");
        services.AddSingleton<RentACar.Application.Common.ISecretProtector,
            RentACar.Infrastructure.Security.DataProtectionSecretProtector>();

        // PII blind-index (KVKK/F2): şifreli PII üzerinde benzersizlik/tam-eşleşme araması için
        // deterministik HMAC anahtarı.
        services.AddSingleton<RentACar.Application.Common.IPiiHasher>(
            new RentACar.Infrastructure.Security.HmacPiiHasher(piiHmacKey));

        // DbContextOptions scope başına kurulur; interceptor'lar oradan eklenir.
        services.AddScoped(sp =>
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>();
            builder.UseNpgsql(appConnectionString);
            builder.AddInterceptors(
                sp.GetRequiredService<TenantConnectionInterceptor>(),
                sp.GetRequiredService<BranchFkInterceptor>(), // audit'ten ÖNCE: çözülen SubeFk denetime yansısın
                sp.GetRequiredService<OfficeBranchInterceptor>(), // FAZ 5-C4: ofis→şube FK
                sp.GetRequiredService<LedgerAmountGuardInterceptor>(), // F8.1a M1: |tutar × kur| < 10^15
                sp.GetRequiredService<AuditSaveChangesInterceptor>());
            return builder.Options;
        });

        // Açık scoped factory (tenant/user'ı oluşturulan context'e taşır).
        services.AddScoped<IDbContextFactory<AppDbContext>, ScopedAppDbContextFactory>();

        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<RentACar.Application.Vehicles.IVehiclePhotoRepository,
            Persistence.Repositories.VehiclePhotoRepository>(); // PR-3
        services.AddScoped<RentACar.Application.Fleet.IPublicBrandingRepository,
            Persistence.Repositories.PublicBrandingRepository>(); // PR-4
        services.AddScoped<RentACar.Application.Blog.IBlogRepository,
            Persistence.Repositories.BlogRepository>(); // PR-6
        services.AddScoped<RentACar.Application.PublicSite.IPublicBookingRequestRepository,
            Persistence.Repositories.PublicBookingRequestRepository>(); // PR-8
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<RentACar.Application.Notifications.INotificationRepository, BildirimRepository>();
        services.AddMemoryCache(); // master/referans cache (Web zaten çağırıyor; idempotent)
        services.AddScoped<RentACar.Application.Common.ITenantCache, RentACar.Infrastructure.Caching.TenantCache>();
        services.AddScoped<ICurrencyRepository, CurrencyRepository>();
        services.AddScoped<RentACar.Application.Kur.IExchangeRateRepository, KurRepository>();
        services.AddScoped<RentACar.Application.Kur.IPinnedRateRepository, SabitKurRepository>();
        services.AddScoped<IRateCardRepository, RateCardRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IAddOnDefinitionRepository, EkHizmetTanimRepository>();
        services.AddScoped<RentACar.Application.FuelKinds.IFuelKindRepository, FuelKindRepository>();
        services.AddScoped<RentACar.Application.TransmissionTypes.ITransmissionTypeRepository, TransmissionTypeRepository>();
        services.AddScoped<RentACar.Application.VehicleColors.IVehicleColorRepository, VehicleColorRepository>();
        services.AddScoped<RentACar.Application.CustomerGroups.ICustomerGroupRepository, CustomerGroupRepository>();
        services.AddScoped<RentACar.Application.InsuranceCompanies.IInsuranceCompanyRepository, InsuranceCompanyRepository>();
        services.AddScoped<RentACar.Application.Banks.IBankRepository, BankRepository>();
        services.AddScoped<RentACar.Application.Departments.IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<RentACar.Application.PaymentTypes.IPaymentTypeRepository, PaymentTypeRepository>();
        services.AddScoped<RentACar.Application.Countries.ICountryRepository, CountryRepository>();
        services.AddScoped<RentACar.Application.Accessories.IAccessoryRepository, AccessoryRepository>();
        services.AddScoped<ICancelReasonRepository, CancelReasonRepository>();
        services.AddScoped<IReservationSourceRepository, ReservationSourceRepository>();
        services.AddScoped<RentACar.Application.VehicleSegments.IVehicleSegmentRepository, VehicleSegmentRepository>();
        services.AddScoped<RentACar.Application.VehicleTypes.IVehicleTypeRepository, VehicleTypeRepository>();
        services.AddScoped<RentACar.Application.VehicleOwners.IVehicleOwnerRepository, VehicleOwnerRepository>();
        services.AddScoped<RentACar.Application.ExpenseCategories.IExpenseCategoryRepository, ExpenseCategoryRepository>();
        services.AddScoped<RentACar.Application.FinancialAccounts.IFinancialAccountRepository, FinancialAccountRepository>();
        services.AddScoped<RentACar.Application.CustomCodes.ICustomCodeRepository, CustomCodeRepository>();
        services.AddScoped<IBrandRepository, BrandRepository>();
        services.AddScoped<IVatRateRepository, KdvRateRepository>();
        services.AddScoped<IVehicleGroupRepository, VehicleGroupRepository>();
        services.AddScoped<RentACar.Application.WebSite.IWebListingRepository,
            Persistence.Repositories.WebIlanRepository>(); // PR-13
        services.AddScoped<RentACar.Application.PlatformBelgeler.IPlatformDocumentRepository,
            Persistence.Repositories.PlatformBelgeRepository>(); // PR-B
        services.AddScoped<RentACar.Application.FirmaDokumanlar.ICompanyFileRepository,
            Persistence.Repositories.FirmaDokumanRepository>(); // firma dokümanları (tenant-owned)
        services.AddScoped<RentACar.Application.Bookings.IContractShareRepository,
            Persistence.Repositories.SozlesmePaylasimRepository>(); // PR-C
        services.AddScoped<RentACar.Application.SiteIcerik.ISiteContentRepository,
            Persistence.Repositories.SiteIcerikRepository>(); // PR-16
        services.AddScoped<IRateMatrixRepository, RateMatrixRepository>();
        services.AddScoped<ICoverageProductRepository, CoverageProductRepository>();
        services.AddScoped<IRentalRuleRepository, RentalRuleRepository>();
        services.AddScoped<RentACar.Application.BrokerYasaklari.IBrokerBanRepository, BrokerYasakRepository>();
        services.AddScoped<RentACar.Application.RezSartlar.IReservationTermRepository, RezSartRepository>();
        services.AddScoped<RentACar.Application.Jobs.IJobRunLogRepository, JobCalismaLogRepository>();
        services.AddScoped<RentACar.Application.TarifeGruplari.ITariffGroupRepository, TarifeGrubuRepository>();
        services.AddScoped<RentACar.Application.TenantSettings.ITenantSettingsRepository,
            Persistence.Repositories.TenantSettingsRepository>();
        services.AddScoped<RentACar.Application.Notifications.IMessageRepository,
            Persistence.Repositories.MesajRepository>();
        // F11.1b — tam değiştirme PUT'larının sürüm deposu (iş/job yolu uygulamaları sürüm bilmez; ayrı sözleşme)
        services.AddScoped<RentACar.Application.TenantSettings.ITenantSettingsVersionStore,
            Persistence.Repositories.TenantSettingsRepository>();
        services.AddScoped<RentACar.Application.Notifications.IMessageTemplateVersionStore,
            Persistence.Repositories.MesajRepository>();
        // F11.1b güvenlik M6 — özel alan adı DNS TXT sahiplik doğrulaması
        services.AddSingleton<RentACar.Application.TenantSettings.IDnsTxtResolver, Integrations.UdpDnsTxtResolver>();
        services.AddScoped<RentACar.Application.TenantSettings.ITenantDomainRepository,
            Persistence.Repositories.TenantDomainRepository>(); // PR-2: public-site host self-servis
        services.AddScoped<RentACar.Application.Personnel.IPersonnelRepository,
            Persistence.Repositories.PersonelRepository>();
        services.AddScoped<RentACar.Application.Personnel.IPersonnelShiftRepository,
            Persistence.Repositories.PersonelVardiyaRepository>();   // FAZ-45
        services.AddScoped<RentACar.Application.Legal.ILegalCaseRepository,
            Persistence.Repositories.HukukDosyaRepository>();
        services.AddScoped<RentACar.Application.Crm.ISurveyRepository,
            Persistence.Repositories.AnketRepository>();
        services.AddScoped<RentACar.Application.Crm.IComplaintRepository,
            Persistence.Repositories.SikayetRepository>();
        services.AddScoped<RentACar.Application.Crm.IAssistanceRequestRepository,
            Persistence.Repositories.AssistansTalepRepository>();   // FAZ-44
        services.AddScoped<RentACar.Application.FiloPlan.IFleetPlanRepository,
            Persistence.Repositories.FiloPlanRepository>();   // FAZ-19
        services.AddScoped<RentACar.Application.MusteriTaksitleri.ICustomerInstallmentRepository,
            Persistence.Repositories.MusteriTaksitRepository>();   // FAZ-66
        services.AddScoped<RentACar.Application.Pricing.ICostQuotationRepository,
            Persistence.Repositories.MaliyetTeklifiRepository>();   // FAZ-74
        services.AddScoped<RentACar.Application.Search.ISearchRepository,
            Persistence.Repositories.SearchRepository>();
        services.AddScoped<RentACar.Application.Periods.IPeriodLockRepository,
            Persistence.Repositories.DonemKilidiRepository>();
        services.AddScoped<RentACar.Application.Periods.IPeriodClosingRepository,
            Persistence.Repositories.DonemKapanisRepository>(); // PR-A atomik/serileştirilmiş kapanış fişi
        services.AddScoped<RentACar.Application.Authorization.IScreenPermissionRepository,
            Persistence.Repositories.ScreenPermissionRepository>();
        services.AddScoped<RentACar.Application.Authorization.IPermissionGroupRepository,
            Persistence.Repositories.YetkiGrupRepository>(); // PR-D — ekran-izni şablonu
        services.AddScoped<IFleetStatusRepository, FleetStatusRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<RentACar.Application.RentalAddOns.IRentalAddOnRepository, RentalAddOnRepository>();
        services.AddScoped<IQuotationRepository, QuotationRepository>();
        services.AddScoped<ICalendarRepository, CalendarRepository>();
        services.AddScoped<ICashRepository, CashRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<RentACar.Application.GelenEFaturalar.IIncomingEInvoiceRepository, GelenEFaturaRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IRegulationRepository, RegulationRepository>();
        // F9.1: generic row-version store for /api/ui full-replacement PUTs (servis/sigorta/fiyat tanımları).
        services.AddScoped<RentACar.Application.Common.IRowVersionStore, RowVersionStore>();
        services.AddScoped<IPenaltyRepository, PenaltyRepository>();
        services.AddScoped<IPenaltyTypeRepository, PenaltyTypeRepository>();
        services.AddScoped<ILedgerPoster, LedgerPoster>();
        services.AddScoped<IVehicleSaleRepository, VehicleSaleRepository>();
        services.AddScoped<RentACar.Application.FiloKiralamalar.IFleetRentalRepository, Persistence.Repositories.FiloKiralamaRepository>(); // roadmap L1
        services.AddScoped<RentACar.Application.AracSiparisleri.IVehicleOrderRepository, Persistence.Repositories.AracSiparisRepository>(); // roadmap L3
        services.AddScoped<RentACar.Application.AracKredileri.IVehicleLoanRepository, Persistence.Repositories.AracKrediRepository>(); // roadmap L4
        services.AddScoped<RentACar.Application.Baflar.IBafRepository, Persistence.Repositories.BafRepository>(); // roadmap L5
        services.AddScoped<RentACar.Application.HesapKodlari.IAccountCodeRepository, Persistence.Repositories.HesapKoduRepository>(); // roadmap N1
        services.AddScoped<RentACar.Application.ServisTanimlari.IServiceDefinitionRepository, Persistence.Repositories.ServisTanimRepository>(); // roadmap N1
        services.AddScoped<RentACar.Application.DropTanimlari.IDropDefinitionRepository, Persistence.Repositories.DropTanimRepository>(); // roadmap N2
        services.AddScoped<RentACar.Application.DolulukFiyat.IOccupancyPriceRuleRepository, Persistence.Repositories.DolulukFiyatKuralRepository>(); // FAZ 3.A7
        services.AddScoped<RentACar.Application.DolulukFiyat.IOccupancyProvider, Persistence.Repositories.OccupancyProvider>();
        services.AddScoped<RentACar.Application.BelgeSablon.IDocumentTemplateRepository, Persistence.Repositories.BelgeSablonRepository>(); // marka-özel PDF metin şablonu
        services.AddScoped<RentACar.Application.FaturaDonemleri.IInvoicePeriodRepository, Persistence.Repositories.FaturaDonemRepository>(); // FAZ 4.2-B1
        services.AddScoped<RentACar.Application.DisHizmetler.IOutsourcedServiceRepository, Persistence.Repositories.DisHizmetRepository>(); // FAZ 4.3
        services.AddScoped<IDamageFileRepository, DamageFileRepository>();
        services.AddScoped<IServiceRecordRepository, ServiceRecordRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserPermissionRepository, KullaniciIzinRepository>(); // izin istisnaları
        services.AddScoped<RentACar.Application.TabloDuzenleri.ITableLayoutRepository, Persistence.Repositories.TabloDuzeniRepository>(); // F3.5 kişisel tablo düzeni
        services.AddScoped<IAvailabilityRepository, AvailabilityRepository>();
        services.AddScoped<IDetailRepository, DetailRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();

        // Kimlik/şifre: paylaşılan login doğrulaması (Web cookie + API JWT ortak kullanır).
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<RentACar.Application.Common.IPasswordHasher, AspNetPasswordHasher>();
        services.AddScoped<LoginService>();

        // Entegrasyon adapter'ları (v1 stub; gerçek impl Faz 2/3'te).
        services.AddIntegrationStubs();
        // E-posta: gerçek SMTP göndericisi stub'ı KOŞULSUZ override eder (son kayıt kazanır).
        // Yapılandırma global config'te değil TENANT satırındadır (TenantSettings.Smtp*), bu yüzden
        // "kurulu mu" kararı DI'da değil gönderim anında verilir — BildirimKanaliService ayar yoksa
        // açık hata döndürür, sessiz başarı üretmez.
        services.AddSingleton<RentACar.Application.Integrations.IEmailSender,
            Integrations.MailKitEmailSender>();

        return services;
    }
}
