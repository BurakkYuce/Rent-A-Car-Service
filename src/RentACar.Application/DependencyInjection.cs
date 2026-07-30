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
        services.AddScoped<VehiclePhotoService>(); // PR-3
        services.AddScoped<RentACar.Application.Fleet.FleetShowcaseService>(); // PR-4
        services.AddScoped<RentACar.Application.Blog.BlogService>(); // PR-6 — halka açık site blog
        services.AddScoped<RentACar.Application.PublicSite.PublicBookingRequestService>(); // PR-8 — site talebi (lead)
        services.AddScoped<CustomerService>();
        services.AddScoped<BranchService>();
        services.AddScoped<RateCardService>();
        services.AddScoped<PricingService>();
        services.AddScoped<RentalQuoteEngine>();
        services.AddScoped<LocationService>();
        services.AddScoped<EkHizmetTanimService>();
        services.AddScoped<RentACar.Application.FuelKinds.FuelKindService>();
        services.AddScoped<RentACar.Application.Kur.KurService>();
        services.AddScoped<RentACar.Application.Kur.SabitKurService>();
        services.AddScoped<RentACar.Application.Kur.KurCozucu>(); // ödeme uçları kur çözümü (1.1)
        services.AddSingleton(RentACar.Application.Reporting.TutSatEsikleri.Varsayilan); // FAZ 2.2 (Web override edebilir)
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
        services.AddScoped<VehicleGroups.VarsayilanGrupCozucu>(); // PR-10 varsayılan araç grubu çözücüsü
        services.AddScoped<WebSite.WebIlanService>();             // PR-13 halka açık site ilan sihirbazı
        services.AddScoped<PlatformBelgeler.PlatformBelgeService>(); // PR-B firma belgeleri (salt-okur)
        services.AddScoped<RateMatrixService>();
        services.AddScoped<CoverageProductService>();
        services.AddScoped<RentalRuleService>();
        services.AddScoped<RentACar.Application.BrokerYasaklari.BrokerYasakService>();
        services.AddScoped<TenantSettings.TenantSettingsService>();
        services.AddScoped<Personnel.PersonelService>();
        services.AddScoped<Legal.HukukDosyaService>();
        services.AddScoped<Crm.AnketService>();
        services.AddScoped<Crm.SikayetService>();
        services.AddScoped<Search.SearchService>();
        services.AddScoped<Periods.DonemKilidiService>();
        services.AddScoped<Periods.IPeriodLockGuard>(sp => sp.GetRequiredService<Periods.DonemKilidiService>());
        services.AddScoped<Periods.DonemKapanisFisiService>(); // PR-A close-lite kapanış fişi
        services.AddScoped<Dashboard.DashboardService>();
        services.AddScoped<Authorization.ScreenPermissionService>();
        services.AddScoped<FleetStatusService>();
        services.AddScoped<ReservationService>();
        services.AddScoped<Bookings.FeeLineService>(); // FAZ 3.A3a sistem ücret satırları (sürücü ücretleri)
        services.AddScoped<Finance.KdvVarsayilan>(); // FAZ 3.A6 tenant varsayılan KDV çözücüsü
        services.AddScoped<DolulukFiyat.DolulukFiyatKuralService>(); // FAZ 3.A7 doluluk çarpanı master
        services.AddScoped<BelgeSablon.BelgeSablonService>(); // marka-özel PDF metin şablonu master
        services.AddScoped<BelgeSablon.BelgeSablonCozumleyici>(); // yazdırma anı şablon çözümü (admin-gate atlar)
        services.AddScoped<FaturaDonemleri.FaturaDonemPlanService>(); // FAZ 4.2-B1 dönem planı
        services.AddScoped<FaturaDonemleri.DonemTahsilatService>(); // FAZ 4.2-B3 kes+tahsilat orkestratörü
        services.AddScoped<DisHizmetler.DisHizmetService>(); // FAZ 4.3 B2B dış hizmet alımı
        services.AddScoped<QuotationService>();
        services.AddScoped<CalendarService>();
        services.AddScoped<RentalService>();
        services.AddScoped<KiraHesapService>(); // kira formu canlı hesap (salt-okunur; GET /kiralar/hesapla)
        services.AddScoped<SozlesmeService>(); // sözleşme çıktısı view-model (HTML+PDF tek kaynak)
        services.AddScoped<RentACar.Application.RentalAddOns.RentalAddOnService>();
        services.AddScoped<CashService>();
        services.AddScoped<Finance.DepozitoService>(); // roadmap I3
        services.AddScoped<InvoiceService>();
        services.AddScoped<RentACar.Application.GelenEFaturalar.GelenEFaturaService>();
        services.AddScoped<ExpenseService>();
        services.AddScoped<RegulationService>();
        services.AddScoped<VadeService>();
        services.AddScoped<Notifications.BildirimService>(); // roadmap G6: bildirim merkezi agrega
        services.AddScoped<PenaltyService>();
        services.AddScoped<PenaltyTypeService>();
        services.AddScoped<HgsReflectionService>();
        services.AddScoped<VehicleSaleService>();
        services.AddScoped<FiloKiralamalar.FiloKiralamaService>(); // roadmap L1
        services.AddScoped<AracSiparisleri.AracSiparisService>(); // roadmap L3
        services.AddScoped<AracKredileri.AracKrediService>(); // roadmap L4
        services.AddScoped<Baflar.BafService>(); // roadmap L5
        services.AddScoped<HesapKodlari.HesapKoduService>(); // roadmap N1
        services.AddScoped<ServisTanimlari.ServisTanimService>(); // roadmap N1
        services.AddScoped<DropTanimlari.DropTanimService>(); // roadmap N2
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
