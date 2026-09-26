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
        services.AddScoped<AddOnDefinitionService>();
        services.AddScoped<RentACar.Application.FuelKinds.FuelKindService>();
        services.AddScoped<RentACar.Application.Kur.ExchangeRateService>();
        services.AddScoped<RentACar.Application.Kur.FixedExchangeRateService>();
        services.AddScoped<RentACar.Application.Kur.ExchangeRateResolver>(); // ödeme uçları kur çözümü (1.1)
        services.AddScoped<RentACar.Application.FinancialAccounts.AccountResolver>(); // FAZ-50 hesap çözümü
        services.AddScoped<RentACar.Application.Finance.BalanceAdjustmentService>();  // FAZ-56
        services.AddSingleton(RentACar.Application.Reporting.TutSatEsikleri.Default); // FAZ 2.2 (Web override edebilir)
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
        services.AddScoped<ReservationSourceRuleService>(); // FAZ-49: kaynak kural matrisi tüketimi
        services.AddScoped<RentACar.Application.VehicleSegments.VehicleSegmentService>();
        services.AddScoped<RentACar.Application.VehicleTypes.VehicleTypeService>();
        services.AddScoped<RentACar.Application.VehicleOwners.VehicleOwnerService>();
        services.AddScoped<RentACar.Application.ExpenseCategories.ExpenseCategoryService>();
        services.AddScoped<RentACar.Application.FinancialAccounts.FinancialAccountService>();
        services.AddScoped<RentACar.Application.CustomCodes.CustomCodeService>();
        services.AddScoped<BrandService>();
        services.AddScoped<CurrencyService>();
        services.AddScoped<VatRateService>();
        services.AddScoped<VehicleGroupService>();
        services.AddScoped<VehicleGroups.DefaultGroupResolver>(); // PR-10 varsayılan araç grubu çözücüsü
        services.AddScoped<WebSite.WebListingService>();             // PR-13 halka açık site ilan sihirbazı
        services.AddScoped<PlatformBelgeler.PlatformDocumentService>(); // PR-B firma belgeleri (salt-okur)
        services.AddScoped<FirmaDokumanlar.CompanyFileService>(); // firmanın KENDİ yüklediği dokümanlar
        services.AddScoped<Bookings.ContractShareService>(); // PR-C sözleşme paylaşım linki
        services.AddScoped<SiteIcerik.SiteContentService>(); // PR-16 halka açık içerik sayfaları + SSS
        services.AddScoped<RateMatrixService>();
        services.AddScoped<CoverageProductService>();
        services.AddScoped<RentalRuleService>();
        services.AddScoped<RentACar.Application.BrokerYasaklari.BrokerBanService>();
        services.AddScoped<RentACar.Application.RezSartlar.ReservationTermService>();
        services.AddScoped<RentACar.Application.Jobs.JobRunLogService>();
        services.AddScoped<RentACar.Application.TarifeGruplari.TariffGroupService>();
        services.AddScoped<TenantSettings.TenantSettingsService>();
        services.AddScoped<Integrations.NotificationChannelService>(); // bildirim omurgası: tenant SMTP/SMS
        services.AddScoped<Notifications.CustomerNotificationService>(); // müşteriye giden bildirim (şablon + idempotent kayıt)
        services.AddScoped<Personnel.PersonnelService>();
        services.AddScoped<Personnel.StaffShiftService>();   // FAZ-45 — vardiya/çalışma grafiği
        services.AddScoped<Legal.LegalCaseService>();
        services.AddScoped<Crm.CrmScopeGuard>();          // r317 M1: CRM şube kapsamı servis katmanında (tek kural)
        services.AddScoped<Crm.SurveyService>();
        services.AddScoped<Crm.ComplaintService>();
        services.AddScoped<Crm.AssistanceRequestService>();   // FAZ-44
        services.AddScoped<FiloPlan.FleetPlanService>();    // FAZ-19
        services.AddScoped<MusteriTaksitleri.CustomerInstallmentService>();   // FAZ-66
        services.AddScoped<Pricing.CostQuotationService>();   // FAZ-74 — kayıtlı maliyet teklifi
        services.AddScoped<Search.SearchService>();
        services.AddScoped<Secim.SelectionService>(); // F1.6 — yeni arayüz seçim/typeahead kaynakları
        services.AddScoped<Periods.PeriodLockService>();
        services.AddScoped<Periods.IPeriodLockGuard>(sp => sp.GetRequiredService<Periods.PeriodLockService>());
        services.AddScoped<Periods.PeriodClosingVoucherService>(); // PR-A close-lite kapanış fişi
        services.AddScoped<Dashboard.DashboardService>();
        services.AddScoped<Authorization.ScreenPermissionService>();
        services.AddScoped<FleetStatusService>();
        services.AddScoped<ReservationService>();
        services.AddScoped<Bookings.FeeLineService>(); // FAZ 3.A3a sistem ücret satırları (sürücü ücretleri)
        services.AddScoped<Finance.VatDefault>(); // FAZ 3.A6 tenant varsayılan KDV çözücüsü
        services.AddScoped<TenantSettings.FormDefaultResolver>(); // FAZ-82 form ön-doldurma varsayılanları (admin-gate atlar)
        services.AddScoped<DolulukFiyat.OccupancyPriceRuleService>(); // FAZ 3.A7 doluluk çarpanı master
        services.AddScoped<BelgeSablon.DocumentTemplateService>(); // marka-özel PDF metin şablonu master
        services.AddScoped<BelgeSablon.DocumentTemplateResolver>(); // yazdırma anı şablon çözümü (admin-gate atlar)
        services.AddScoped<FaturaDonemleri.InvoicePeriodPlanService>(); // FAZ 4.2-B1 dönem planı
        services.AddScoped<FaturaDonemleri.PeriodCollectionService>();
        services.AddScoped<FaturaDonemleri.AutoCollectionService>(); // FAZ-30 elle tetikleme
        services.AddScoped<DisHizmetler.OutsourcedServiceService>(); // FAZ 4.3 B2B dış hizmet alımı
        services.AddScoped<QuotationService>();
        services.AddScoped<CalendarService>();
        services.AddScoped<RentalService>();
        services.AddScoped<RentalCalculationService>(); // kira formu canlı hesap (salt-okunur; GET /kiralar/hesapla)
        services.AddScoped<ContractService>(); // sözleşme çıktısı view-model (HTML+PDF tek kaynak)
        services.AddScoped<RentACar.Application.RentalAddOns.RentalAddOnService>();
        services.AddScoped<CashService>();
        services.AddScoped<Finance.DepositService>(); // roadmap I3
        services.AddScoped<InvoiceService>();
        services.AddScoped<RentACar.Application.GelenEFaturalar.IncomingEInvoiceService>();
        services.AddScoped<ExpenseService>();
        services.AddScoped<RegulationService>();
        services.AddScoped<DueService>();
        services.AddScoped<Notifications.InAppNotificationService>(); // roadmap G6: bildirim merkezi agrega
        services.AddScoped<PenaltyService>();
        services.AddScoped<PenaltyTypeService>();
        services.AddScoped<HgsReflectionService>();
        services.AddScoped<VehicleSaleService>();
        services.AddScoped<FiloKiralamalar.FleetRentalService>(); // roadmap L1
        services.AddScoped<AracSiparisleri.VehicleOrderService>(); // roadmap L3
        services.AddScoped<AracKredileri.VehicleLoanService>(); // roadmap L4
        services.AddScoped<Baflar.BafService>(); // roadmap L5
        services.AddScoped<HesapKodlari.AccountCodeService>(); // roadmap N1
        services.AddScoped<ServisTanimlari.ServiceDefinitionService>(); // roadmap N1
        services.AddScoped<DropTanimlari.DropDefinitionService>(); // roadmap N2
        services.AddScoped<DamageFileService>();
        services.AddScoped<ServiceRecordService>();
        services.AddScoped<ReportService>();
        services.AddScoped<UserService>();
        services.AddScoped<UserPermissionService>(); // kullanıcı-bazlı izin istisnaları
        services.AddScoped<TabloDuzenleri.TableLayoutService>(); // F3.5 kişisel tablo düzeni
        services.AddScoped<AvailabilityService>();
        services.AddScoped<DetailService>();
        services.AddScoped<AuditService>();
        return services;
    }
}
