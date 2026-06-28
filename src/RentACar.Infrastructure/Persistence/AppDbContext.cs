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
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<RentalContract> Rentals => Set<RentalContract>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AccountLedgerEntry> AccountLedgerEntries => Set<AccountLedgerEntry>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<InsurancePolicy> InsurancePolicies => Set<InsurancePolicy>();
    public DbSet<MtvRecord> MtvRecords => Set<MtvRecord>();
    public DbSet<InspectionRecord> InspectionRecords => Set<InspectionRecord>();
    public DbSet<Penalty> Penalties => Set<Penalty>();
    public DbSet<VehicleSale> VehicleSales => Set<VehicleSale>();
    public DbSet<DamageFile> DamageFiles => Set<DamageFile>();
    public DbSet<ServiceRecord> ServiceRecords => Set<ServiceRecord>();
    public DbSet<ServiceLine> ServiceLines => Set<ServiceLine>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ---- Tenant (platform) ----
        b.Entity<Tenant>(e =>
        {
            e.ToTable("Tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).IsRequired().HasMaxLength(64);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
        });

        // ---- User (platform; tenant'a bağlı ama RLS yok) ----
        b.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserName).IsRequired().HasMaxLength(128);
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.Rol).HasConversion<int>();
            e.Property(x => x.AtanmisSube).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.UserName }).IsUnique();
        });

        // ---- Branch / Şube (tenant-owned; master sözlük) ----
        b.Entity<Branch>(e =>
        {
            e.ToTable("Branches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Adres).HasMaxLength(512);
            e.Property(x => x.Telefon).HasMaxLength(32);
            // Kod tenant içinde benzersiz (doğal iş anahtarı; servis büyük harfe normalize eder).
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- RateCard / Tarife (tenant-owned; fiyat master) ----
        b.Entity<RateCard>(e =>
        {
            e.ToTable("RateCards");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Grup).IsRequired().HasMaxLength(64);
            e.Property(x => x.GunlukUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.Doviz).HasMaxLength(3);
            // Kod tenant içinde benzersiz (servis büyük harfe normalize eder).
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            // Lookup: grup bazlı arama (büyük/küçük harf duyarsız ILike ile).
            e.HasIndex(x => new { x.TenantId, x.Grup });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- CancelReason / İptal sebebi (tenant-owned; master sözlük) ----
        b.Entity<CancelReason>(e =>
        {
            e.ToTable("IptalSebepleri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- ReservationSource / Rezervasyon kaynağı (tenant-owned; master sözlük) ----
        b.Entity<ReservationSource>(e =>
        {
            e.ToTable("RezervasyonKaynaklari");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- VehicleSegment / Araç segment (tenant-owned; master sözlük) ----
        b.Entity<VehicleSegment>(e =>
        {
            e.ToTable("Segmentler");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- VehicleType / Araç tip (tenant-owned; master sözlük) ----
        b.Entity<VehicleType>(e =>
        {
            e.ToTable("AracTipleri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Marka).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- VehicleOwner / Araç sahip (tenant-owned; master sözlük) ----
        b.Entity<VehicleOwner>(e =>
        {
            e.ToTable("AracSahipleri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Tur).HasMaxLength(32);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- ExpenseCategory / Gider türü (tenant-owned; master sözlük) ----
        b.Entity<ExpenseCategory>(e =>
        {
            e.ToTable("GiderTurleri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- FinancialAccount / Kasa-Banka hesap (tenant-owned; master sözlük) ----
        b.Entity<FinancialAccount>(e =>
        {
            e.ToTable("Hesaplar");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Tur).HasMaxLength(32);
            e.Property(x => x.Doviz).HasMaxLength(3);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- CustomCode / Özel kod (tenant-owned; master sözlük) ----
        b.Entity<CustomCode>(e =>
        {
            e.ToTable("OzelKodlar");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- FuelKind / Yakıt türü (tenant-owned; master sözlük) ----
        b.Entity<FuelKind>(e =>
        {
            e.ToTable("YakitTurleri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- TransmissionType / Vites türü (tenant-owned; master sözlük) ----
        b.Entity<TransmissionType>(e =>
        {
            e.ToTable("VitesTurleri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- VehicleColor / Renk (tenant-owned; master sözlük) ----
        b.Entity<VehicleColor>(e =>
        {
            e.ToTable("Renkler");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- CustomerGroup / Müşteri grubu (tenant-owned; master sözlük) ----
        b.Entity<CustomerGroup>(e =>
        {
            e.ToTable("MusteriGruplari");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- InsuranceCompany / Sigorta şirketi (tenant-owned; master sözlük) ----
        b.Entity<InsuranceCompany>(e =>
        {
            e.ToTable("SigortaSirketleri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Telefon).HasMaxLength(32);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Bank / Banka (tenant-owned; master sözlük) ----
        b.Entity<Bank>(e =>
        {
            e.ToTable("Bankalar");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Department / Departman (tenant-owned; master sözlük) ----
        b.Entity<Department>(e =>
        {
            e.ToTable("Departmanlar");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- PaymentType / Ödeme tipi (tenant-owned; master sözlük) ----
        b.Entity<PaymentType>(e =>
        {
            e.ToTable("OdemeTipleri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Country / Ülke (tenant-owned; master sözlük) ----
        b.Entity<Country>(e =>
        {
            e.ToTable("Ulkeler");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Accessory / Aksesuar (tenant-owned; master sözlük) ----
        b.Entity<Accessory>(e =>
        {
            e.ToTable("Aksesuarlar");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- EkHizmetTanim / Ek hizmet tanımı (tenant-owned; master sözlük) ----
        b.Entity<EkHizmetTanim>(e =>
        {
            e.ToTable("EkHizmetTanimlari");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.BirimUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Brand / Marka (tenant-owned; master sözlük) ----
        b.Entity<Brand>(e =>
        {
            e.ToTable("Markalar");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        // ---- Currency / Döviz (tenant-owned; master sözlük) ----
        b.Entity<Currency>(e =>
        {
            e.ToTable("Dovizler");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(3);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Sembol).HasMaxLength(8);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Location / Ofis (tenant-owned; master sözlük) ----
        b.Entity<Location>(e =>
        {
            e.ToTable("Locations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Adres).HasMaxLength(512);
            e.Property(x => x.Telefon).HasMaxLength(32);
            e.Property(x => x.Sube).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Vehicle (tenant-owned) ----
        b.Entity<Vehicle>(e =>
        {
            e.ToTable("Vehicles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Plaka).IsRequired().HasMaxLength(16);
            e.Property(x => x.Marka).HasMaxLength(64);
            e.Property(x => x.Grup).HasMaxLength(64);
            e.Property(x => x.Sube).HasMaxLength(64);
            e.Property(x => x.Durum).HasConversion<int>();
            e.Property(x => x.Yakit).HasConversion<int>();
            // Plaka tenant içinde benzersiz (doğal iş anahtarı).
            e.HasIndex(x => new { x.TenantId, x.Plaka }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Customer / Cari (tenant-owned) ----
        b.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Tip).HasConversion<int>();
            e.Property(x => x.Ad).HasMaxLength(128);
            e.Property(x => x.Soyad).HasMaxLength(128);
            e.Property(x => x.TcKimlik).HasMaxLength(11);
            e.Property(x => x.Unvan).HasMaxLength(256);
            e.Property(x => x.VergiDairesi).HasMaxLength(128);
            e.Property(x => x.VergiNo).HasMaxLength(16);
            e.Property(x => x.CepTel).HasMaxLength(32);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Il).HasMaxLength(64);
            e.Property(x => x.Ilce).HasMaxLength(64);
            e.Property(x => x.Adres).HasMaxLength(512);
            e.Property(x => x.Tarife).HasMaxLength(64);
            e.Property(x => x.RiskLimiti).HasColumnType("numeric(19,4)");
            e.Ignore(x => x.DisplayName);
            // Tenant içinde benzersiz — yalnız dolu olduğunda (kısmi unique index).
            e.HasIndex(x => new { x.TenantId, x.TcKimlik })
                .IsUnique()
                .HasFilter("\"TcKimlik\" IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.VergiNo })
                .IsUnique()
                .HasFilter("\"VergiNo\" IS NOT NULL");
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Reservation (tenant-owned) ----
        b.Entity<Reservation>(e =>
        {
            e.ToTable("Reservations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ReservationNo).IsRequired().HasMaxLength(32);
            e.Property(x => x.Durum).HasConversion<int>();
            e.Property(x => x.CikisOfisi).HasMaxLength(64);
            e.Property(x => x.DonusOfisi).HasMaxLength(64);
            e.Property(x => x.GunlukUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.FazlaKmUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.YakitBirimUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.Aciklama).HasMaxLength(1024);
            e.HasIndex(x => new { x.TenantId, x.ReservationNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.VehicleId });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Quotation / Teklif (tenant-owned; operasyonel, güncellenebilir) ----
        b.Entity<Quotation>(e =>
        {
            e.ToTable("Quotations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.No).IsRequired().HasMaxLength(32);
            e.Property(x => x.Durum).HasConversion<int>();
            e.Property(x => x.CikisOfisi).HasMaxLength(64);
            e.Property(x => x.DonusOfisi).HasMaxLength(64);
            e.Property(x => x.GunlukUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.FazlaKmUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.YakitBirimUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.Aciklama).HasMaxLength(1024);
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.VehicleId });
            e.HasIndex(x => new { x.TenantId, x.Durum });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- RentalContract (tenant-owned) ----
        b.Entity<RentalContract>(e =>
        {
            e.ToTable("Rentals");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SozlesmeNo).IsRequired().HasMaxLength(32);
            e.Property(x => x.Durum).HasConversion<int>();
            e.Property(x => x.CikisOfisi).HasMaxLength(64);
            e.Property(x => x.DonusOfisi).HasMaxLength(64);
            e.Property(x => x.GunlukUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.GenelToplam).HasColumnType("numeric(19,4)");
            e.Property(x => x.Tahsilat).HasColumnType("numeric(19,4)");
            e.Property(x => x.Bakiye).HasColumnType("numeric(19,4)");
            e.Property(x => x.FazlaKmUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.FazlaKmBedeli).HasColumnType("numeric(19,4)");
            e.Property(x => x.YakitBirimUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.YakitBedeli).HasColumnType("numeric(19,4)");
            e.Property(x => x.UzatmaBedeli).HasColumnType("numeric(19,4)");
            e.Property(x => x.Aciklama).HasMaxLength(1024);
            e.HasIndex(x => new { x.TenantId, x.SozlesmeNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.VehicleId });
            e.HasQueryFilter(x => x.TenantId == TenantId);
            // Double-booking exclusion constraint + generated Period kolonu migration'da (raw SQL).
        });

        // ---- TenantSequence (tenant-owned; boşluksuz sıra) ----
        b.Entity<TenantSequence>(e =>
        {
            e.ToTable("TenantSequences");
            e.HasKey(x => new { x.TenantId, x.Name });
            e.Property(x => x.Name).HasMaxLength(64);
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Regülasyon (tenant-owned; güncellenebilir, mali belge değil) ----
        b.Entity<InsurancePolicy>(e =>
        {
            e.ToTable("InsurancePolicies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Tip).HasConversion<int>();
            e.Property(x => x.PoliceNo).HasMaxLength(64);
            e.Property(x => x.Firma).HasMaxLength(128);
            e.Property(x => x.Acenta).HasMaxLength(128);
            e.Property(x => x.Prim).HasColumnType("numeric(19,4)");
            e.Property(x => x.Currency).HasMaxLength(3);
            e.HasIndex(x => new { x.TenantId, x.VehicleId });
            e.HasIndex(x => new { x.TenantId, x.Bitis });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });
        b.Entity<MtvRecord>(e =>
        {
            e.ToTable("MtvRecords");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Donem).IsRequired().HasMaxLength(16);
            e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
            e.HasIndex(x => new { x.TenantId, x.VehicleId });
            e.HasIndex(x => new { x.TenantId, x.Vade });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });
        b.Entity<InspectionRecord>(e =>
        {
            e.ToTable("InspectionRecords");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Ucret).HasColumnType("numeric(19,4)");
            e.HasIndex(x => new { x.TenantId, x.VehicleId });
            e.HasIndex(x => new { x.TenantId, x.Bitis });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Penalty / Ceza (tenant-owned; başlık güncellenebilir, yansıtma defteri immutable) ----
        b.Entity<Penalty>(e =>
        {
            e.ToTable("Penalties");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.No).IsRequired().HasMaxLength(32);
            e.Property(x => x.CezaTuru).IsRequired().HasMaxLength(128);
            e.Property(x => x.Sebep).HasMaxLength(512);
            e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.Durum).HasConversion<int>();
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CariId });
            e.HasIndex(x => new { x.TenantId, x.VehicleId });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- VehicleSale / Araç Satış (tenant-owned; mali belge → DB-immutable) ----
        b.Entity<VehicleSale>(e =>
        {
            e.ToTable("VehicleSales");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.No).IsRequired().HasMaxLength(32);
            e.Property(x => x.NoterNo).HasMaxLength(64);
            e.Property(x => x.SatisNet).HasColumnType("numeric(19,4)");
            e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
            e.Property(x => x.KdvTutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.GenelToplam).HasColumnType("numeric(19,4)");
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.Property(x => x.Durum).HasConversion<int>();
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.AliciCariId });
            // Bir araç EN FAZLA bir kez 'Tamamlandi' satılabilir (çift satış güvencesi).
            e.HasIndex(x => new { x.TenantId, x.VehicleId })
                .IsUnique()
                .HasFilter("\"Durum\" = 0");
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- DamageFile / BAF (tenant-owned; onay akışı, mali belge DEĞİL → güncellenebilir) ----
        b.Entity<DamageFile>(e =>
        {
            e.ToTable("DamageFiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.No).IsRequired().HasMaxLength(32);
            e.Property(x => x.Aciklama).HasMaxLength(1024);
            e.Property(x => x.TahminiTutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.Durum).HasConversion<int>();
            e.Property(x => x.OnayNotu).HasMaxLength(512);
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.VehicleId });
            e.HasIndex(x => new { x.TenantId, x.Durum });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- ServiceRecord / Servis-Bakım (tenant-owned; operasyonel, güncellenebilir) ----
        b.Entity<ServiceRecord>(e =>
        {
            e.ToTable("ServiceRecords");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.No).IsRequired().HasMaxLength(32);
            e.Property(x => x.Tip).HasConversion<int>();
            e.Property(x => x.Durum).HasConversion<int>();
            e.Property(x => x.HasarSorumlu).HasConversion<int>();
            e.Property(x => x.AtolyeAdi).HasMaxLength(128);
            e.Property(x => x.Aciklama).HasMaxLength(1024);
            e.Property(x => x.KusurOrani).HasColumnType("numeric(5,4)");
            e.Property(x => x.ToplamIscilik).HasColumnType("numeric(19,4)");
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.VehicleId });
            e.HasIndex(x => new { x.TenantId, x.Durum });
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ServiceRecordId);
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });
        b.Entity<ServiceLine>(e =>
        {
            e.ToTable("ServiceLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Aciklama).IsRequired().HasMaxLength(512);
            e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
            e.HasIndex(x => new { x.TenantId, x.ServiceRecordId });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- AuditLog (tenant-owned) ----
        b.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.EntityName).IsRequired().HasMaxLength(128);
            e.Property(x => x.EntityId).IsRequired().HasMaxLength(64);
            e.Property(x => x.Action).HasConversion<int>();
            e.Property(x => x.UserName).HasMaxLength(128);
            e.Property(x => x.OldValues).HasColumnType("jsonb");
            e.Property(x => x.NewValues).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- AccountLedgerEntry (tenant-owned; append-only + DB-immutable) ----
        b.Entity<AccountLedgerEntry>(e =>
        {
            e.ToTable("AccountLedgerEntries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.AccountType).HasConversion<int>();
            e.Property(x => x.Direction).HasConversion<int>();
            e.Property(x => x.Description).HasMaxLength(512);
            e.Property(x => x.SourceType).IsRequired().HasMaxLength(32);
            e.Ignore(x => x.SignedBase);
            e.ComplexProperty(x => x.Amount, m =>
            {
                m.Property(p => p.Amount).HasColumnName("Amount_Value").HasColumnType("numeric(19,4)");
                m.Property(p => p.Currency).HasColumnName("Amount_Currency").HasMaxLength(3);
                m.Property(p => p.Rate).HasColumnName("Amount_Rate").HasColumnType("numeric(19,6)");
            });
            e.HasIndex(x => new { x.TenantId, x.AccountType, x.AccountRef });
            e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId });
            // HGS yansıtma idempotency: aynı (cari,plaka,dönem) deterministik SourceId ile
            // tek kez yazılabilir. Dengeli çift (Borç/Alacak) Direction'la ayrışır → ikisi de
            // geçer; tekrar eden yansıtma çakışır. Yalnız 'Hgs' kaynak türü için (kısmi).
            e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction })
                .IsUnique()
                .HasFilter("\"SourceType\" = 'Hgs'");
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- CashTransaction (tenant-owned; tahsilat belgesi) ----
        b.Entity<CashTransaction>(e =>
        {
            e.ToTable("CashTransactions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.No).IsRequired().HasMaxLength(32);
            e.Property(x => x.Tip).HasConversion<int>();
            e.Property(x => x.KarsiHesap).HasConversion<int>();
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.ComplexProperty(x => x.Amount, m =>
            {
                m.Property(p => p.Amount).HasColumnName("Amount_Value").HasColumnType("numeric(19,4)");
                m.Property(p => p.Currency).HasColumnName("Amount_Currency").HasMaxLength(3);
                m.Property(p => p.Rate).HasColumnName("Amount_Rate").HasColumnType("numeric(19,6)");
            });
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CariId });
            // Idempotency: bir işlemin EN FAZLA bir ters kaydı olabilir (yarış güvencesi).
            e.HasIndex(x => new { x.TenantId, x.TersAlinanId })
                .IsUnique()
                .HasFilter("\"TersAlinanId\" IS NOT NULL");
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Invoice (tenant-owned; append-only + DB-immutable) ----
        b.Entity<Invoice>(e =>
        {
            e.ToTable("Invoices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.No).IsRequired().HasMaxLength(32);
            e.Property(x => x.Durum).HasConversion<int>();
            e.Property(x => x.NetTutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.KdvTutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.GenelToplam).HasColumnType("numeric(19,4)");
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
            e.Property(x => x.EFaturaEttn).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CariId });
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.InvoiceId);
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Expense / Gider (tenant-owned; append-only + DB-immutable) ----
        b.Entity<Expense>(e =>
        {
            e.ToTable("Expenses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.No).IsRequired().HasMaxLength(32);
            e.Property(x => x.Tip).HasConversion<int>();
            e.Property(x => x.OdemeYontemi).HasConversion<int>();
            e.Property(x => x.KasaBankaHesap).HasConversion<int>();
            e.Property(x => x.Sube).HasMaxLength(64);
            e.Property(x => x.EvrakNo).HasMaxLength(64);
            e.Property(x => x.NetTutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
            e.Property(x => x.KdvTutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.GenelToplam).HasColumnType("numeric(19,4)");
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.VehicleId });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        b.Entity<InvoiceLine>(e =>
        {
            e.ToTable("InvoiceLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.Property(x => x.Miktar).HasColumnType("numeric(19,4)");
            e.Property(x => x.BirimNetFiyat).HasColumnType("numeric(19,4)");
            e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
            e.Property(x => x.SatirNet).HasColumnType("numeric(19,4)");
            e.Property(x => x.SatirKdv).HasColumnType("numeric(19,4)");
            e.Property(x => x.SatirToplam).HasColumnType("numeric(19,4)");
            e.HasIndex(x => new { x.TenantId, x.InvoiceId });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });
    }
}
