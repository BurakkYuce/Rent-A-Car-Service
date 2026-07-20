using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Uygulama DbContext'i. Kısa-ömürlü kullanılır (Blazor circuit'i boyunca scoped DEĞİL;
/// <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/> ile her işlemde
/// yeni context). Constructor, tenant'ı bir kez yakalar (context ömrü kısa olduğundan
/// tenant sabittir) → global query filter bu sabit değere göre çalışır.
/// </summary>
/// <remarks>
/// YENİ TABLO eklerken: config'i BU DOSYAYA DEĞİL, Configurations/ altına
/// IEntityTypeConfiguration sınıfı olarak ekle; HasQueryFilter YAZMA —
/// OnModelCreating'deki merkezi döngü tüm ITenantOwned entity'lere tenant
/// filtresini otomatik uygular (tek tek unutulamaz).
/// </remarks>
public sealed class AppDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantContext tenantContext,
        ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
        // Kısa-ömürlü context: tenant'ı oluştururken yakala (query filter + RLS bunu kullanır).
        TenantId = tenantContext.TenantId ?? Guid.Empty;
    }

    /// <summary>Bu context örneğinin tenant'ı. Query filter ve RLS GUC'u buna dayanır.</summary>
    public Guid TenantId { get; }

    public Guid? CurrentUserId => _currentUser.UserId;
    public string? CurrentUserName => _currentUser.UserName;

    // Platform tabloları (RLS yok)
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();

    // Tenant-owned tablolar (EF filter + Postgres RLS)
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Bildirim> Bildirimler => Set<Bildirim>();
    public DbSet<RateCard> RateCards => Set<RateCard>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<EkHizmetTanim> EkHizmetTanimlari => Set<EkHizmetTanim>();
    public DbSet<FuelKind> FuelKinds => Set<FuelKind>();
    public DbSet<TransmissionType> TransmissionTypes => Set<TransmissionType>();
    public DbSet<VehicleColor> VehicleColors => Set<VehicleColor>();
    public DbSet<CustomerGroup> CustomerGroups => Set<CustomerGroup>();
    public DbSet<InsuranceCompany> InsuranceCompanies => Set<InsuranceCompany>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<PaymentType> PaymentTypes => Set<PaymentType>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Accessory> Accessories => Set<Accessory>();
    public DbSet<CancelReason> CancelReasons => Set<CancelReason>();
    public DbSet<ReservationSource> ReservationSources => Set<ReservationSource>();
    public DbSet<VehicleSegment> VehicleSegments => Set<VehicleSegment>();
    public DbSet<VehicleType> VehicleTypes => Set<VehicleType>();
    public DbSet<VehicleOwner> VehicleOwners => Set<VehicleOwner>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<CustomCode> CustomCodes => Set<CustomCode>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<KurKaydi> KurKayitlari => Set<KurKaydi>();   // TCMB günlük kur (paylaşımlı/platform)
    public DbSet<SabitKur> SabitKurlar => Set<SabitKur>();    // tenant kur sabitleme (tenant-owned/RLS)
    public DbSet<PenaltyType> CezaTurleri => Set<PenaltyType>();
    public DbSet<KdvRate> KdvOranlari => Set<KdvRate>();
    public DbSet<VehicleGroup> VehicleGroups => Set<VehicleGroup>();
    public DbSet<RateMatrix> RateMatrices => Set<RateMatrix>();
    public DbSet<CoverageProduct> CoverageProducts => Set<CoverageProduct>();
    public DbSet<RentalRule> RentalRules => Set<RentalRule>();
    public DbSet<BrokerYasak> BrokerYasaklar => Set<BrokerYasak>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<RentalContract> Rentals => Set<RentalContract>();
    public DbSet<RentalAddOn> RentalAddOns => Set<RentalAddOn>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AccountLedgerEntry> AccountLedgerEntries => Set<AccountLedgerEntry>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<DepozitoIrat> DepozitoIratlar => Set<DepozitoIrat>(); // FAZ 1.2
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<GelenEFatura> GelenEFaturalar => Set<GelenEFatura>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<InsurancePolicy> InsurancePolicies => Set<InsurancePolicy>();
    public DbSet<MtvRecord> MtvRecords => Set<MtvRecord>();
    public DbSet<InspectionRecord> InspectionRecords => Set<InspectionRecord>();
    public DbSet<Penalty> Penalties => Set<Penalty>();
    public DbSet<VehicleSale> VehicleSales => Set<VehicleSale>();
    public DbSet<FiloKiralama> FiloKiralamalar => Set<FiloKiralama>(); // roadmap L1
    public DbSet<AracSiparis> AracSiparisleri => Set<AracSiparis>(); // roadmap L3
    public DbSet<AracKredi> AracKredileri => Set<AracKredi>(); // roadmap L4
    public DbSet<Baf> Baflar => Set<Baf>(); // roadmap L5
    public DbSet<HesapKodu> HesapKodlari => Set<HesapKodu>(); // roadmap N1
    public DbSet<ServisTanim> ServisTanimlari => Set<ServisTanim>(); // roadmap N1
    public DbSet<DropTanim> DropTanimlari => Set<DropTanim>(); // roadmap N2
    public DbSet<DamageFile> DamageFiles => Set<DamageFile>();
    public DbSet<ServiceRecord> ServiceRecords => Set<ServiceRecord>();
    public DbSet<ServiceLine> ServiceLines => Set<ServiceLine>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();
    public DbSet<WhatsAppGonderim> WhatsAppGonderimler => Set<WhatsAppGonderim>();
    public DbSet<Personel> Personeller => Set<Personel>();
    public DbSet<HukukDosya> HukukDosyalari => Set<HukukDosya>();
    public DbSet<Anket> Anketler => Set<Anket>();
    public DbSet<Sikayet> Sikayetler => Set<Sikayet>();
    public DbSet<DonemKilidi> DonemKilitleri => Set<DonemKilidi>();
    public DbSet<ScreenPermission> EkranYetkileri => Set<ScreenPermission>();
    public DbSet<YetkiGrup> YetkiGruplari => Set<YetkiGrup>(); // PR-D — ekran-izni şablonu
    public DbSet<VehicleKmLog> KmLoglari => Set<VehicleKmLog>(); // FAZ 2.5 — km zaman serisi
    public DbSet<DolulukFiyatKural> DolulukFiyatKurallari => Set<DolulukFiyatKural>(); // FAZ 3.A7
    public DbSet<FaturaDonemi> FaturaDonemleri => Set<FaturaDonemi>(); // FAZ 4.2-B1
    public DbSet<DisHizmetAlimi> DisHizmetAlimlari => Set<DisHizmetAlimi>(); // FAZ 4.3
    public DbSet<BelgeSablon> BelgeSablonlari => Set<BelgeSablon>(); // marka-özel PDF metin şablonları

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Tüm entity config'leri Configurations/ altındaki IEntityTypeConfiguration
        // sınıflarından uygulanır (alan-gruplu dosyalar; bu dosyada inline config YOK).
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Tüm ITenantOwned entity'lere tenant filtresi MERKEZİ uygulanır (tek tek unutulamaz).
        // Expression.Constant(this) → EF context-instance'ı tanır ve sorgu başına geçerli tenant'ı parametreler
        // (inline x => x.TenantId == TenantId ile birebir aynı mekanizma).
        // NOT: Bu döngü ApplyConfigurationsFromAssembly'den SONRA çalışmalı (entity type'lar kayıtlı olmalı).
        foreach (var et in b.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(et.ClrType) || et.BaseType is not null) continue;
            var p = Expression.Parameter(et.ClrType, "x");
            var body = Expression.Equal(
                Expression.Property(p, nameof(ITenantOwned.TenantId)),
                Expression.Property(Expression.Constant(this), nameof(TenantId)));
            b.Entity(et.ClrType).HasQueryFilter(Expression.Lambda(body, p));
        }
    }
}
