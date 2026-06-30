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
    public DbSet<PenaltyType> CezaTurleri => Set<PenaltyType>();
    public DbSet<KdvRate> KdvOranlari => Set<KdvRate>();
    public DbSet<VehicleGroup> VehicleGroups => Set<VehicleGroup>();
    public DbSet<RateMatrix> RateMatrices => Set<RateMatrix>();
    public DbSet<CoverageProduct> CoverageProducts => Set<CoverageProduct>();
    public DbSet<RentalRule> RentalRules => Set<RentalRule>();
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
    public DbSet<Invoice> Invoices => Set<Invoice>();
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
    public DbSet<Personel> Personeller => Set<Personel>();
    public DbSet<HukukDosya> HukukDosyalari => Set<HukukDosya>();
    public DbSet<Anket> Anketler => Set<Anket>();
    public DbSet<Sikayet> Sikayetler => Set<Sikayet>();
    public DbSet<DonemKilidi> DonemKilitleri => Set<DonemKilidi>();
    public DbSet<ScreenPermission> EkranYetkileri => Set<ScreenPermission>();

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
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.AtanmisSubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK)
            e.HasIndex(x => x.AtanmisSubeId);
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
            // roadmap K3 derinlik
            e.Property(x => x.Eposta).HasMaxLength(128);
            e.Property(x => x.Il).HasMaxLength(64);
            e.Property(x => x.Ilce).HasMaxLength(64);
            e.Property(x => x.Yetkili).HasMaxLength(128);
            e.Property(x => x.CalismaSaatleri).HasMaxLength(64);
            e.Property(x => x.KomisyonOran).HasColumnType("numeric(5,4)");
            e.Property(x => x.EvrakNoOnek).HasMaxLength(16);
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
            e.Property(x => x.Vites).HasMaxLength(32);  // roadmap K1
            e.Property(x => x.Yakit).HasMaxLength(32);  // roadmap K1
            e.Property(x => x.Grup).HasMaxLength(64);   // roadmap K1
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

        // ---- TenantSettings / Ayarlar (tenant-owned; tenant başına TEK satır, roadmap D1) ----
        // Sır alanları (*Enc) ŞİFRELİ cipher saklar (servis ISecretProtector ile); kolon düz metin değildir.
        b.Entity<TenantSettings>(e =>
        {
            e.ToTable("Ayarlar");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.FirmaUnvan).HasMaxLength(256);
            e.Property(x => x.FirmaVergiDairesi).HasMaxLength(128);
            e.Property(x => x.FirmaVergiNo).HasMaxLength(32);
            e.Property(x => x.FirmaAdres).HasMaxLength(512);
            e.Property(x => x.FirmaTel).HasMaxLength(64);
            e.Property(x => x.FirmaEmail).HasMaxLength(128);
            e.Property(x => x.EFaturaKullanici).HasMaxLength(128);
            e.Property(x => x.EFaturaSifreEnc).HasMaxLength(1024);
            e.Property(x => x.SmsBaslik).HasMaxLength(64);
            e.Property(x => x.SmsApiKeyEnc).HasMaxLength(1024);
            e.Property(x => x.PosMerchantId).HasMaxLength(128);
            e.Property(x => x.PosApiKeyEnc).HasMaxLength(1024);
            e.HasIndex(x => x.TenantId).IsUnique(); // tenant başına tek satır
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Personel (tenant-owned; master, roadmap C1) ----
        // PII (*Enc) ŞİFRELİ cipher saklar (servis ISecretProtector ile); kolon düz metin değildir.
        b.Entity<Personel>(e =>
        {
            e.ToTable("Personeller");
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK; çapraz-tenant referans imkansız)
            e.HasIndex(x => x.SubeId);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Soyad).IsRequired().HasMaxLength(128);
            e.Property(x => x.TcKimlikEnc).HasMaxLength(1024);
            e.Property(x => x.SurucuBelgeNo).HasMaxLength(64);
            e.Property(x => x.MaasEnc).HasMaxLength(1024);
            e.Property(x => x.Sube).HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- HukukDosya (tenant-owned; master, roadmap C2) ----
        b.Entity<HukukDosya>(e =>
        {
            e.ToTable("HukukDosyalari");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.DosyaNo).IsRequired().HasMaxLength(64);
            e.Property(x => x.Avukat).HasMaxLength(128);
            e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.Tur).HasConversion<int>();
            e.Property(x => x.Durum).HasConversion<int>();
            e.Property(x => x.Aciklama).HasMaxLength(1024);
            e.HasIndex(x => new { x.TenantId, x.DosyaNo }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Anket / Sikayet (tenant-owned; CRM, roadmap C3) ----
        b.Entity<Anket>(e =>
        {
            e.ToTable("Anketler");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Yorum).HasMaxLength(1024);
            e.Property(x => x.Kaynak).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.Tarih });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });
        b.Entity<Sikayet>(e =>
        {
            e.ToTable("Sikayetler");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Konu).IsRequired().HasMaxLength(256);
            e.Property(x => x.Detay).HasMaxLength(2048);
            e.Property(x => x.Durum).HasConversion<int>();
            e.Property(x => x.Cozum).HasMaxLength(2048);
            e.HasIndex(x => new { x.TenantId, x.Tarih });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- DonemKilidi / Dönem kapanışı (tenant-owned; tenant başına TEK satır, roadmap D2) ----
        b.Entity<DonemKilidi>(e =>
        {
            e.ToTable("DonemKilitleri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.TenantId).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- ScreenPermission / Ekran yetki override (tenant-owned, roadmap E3) ----
        b.Entity<ScreenPermission>(e =>
        {
            e.ToTable("EkranYetkileri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.EkranKodu).IsRequired().HasMaxLength(64);
            e.Property(x => x.AllowedRolesCsv).HasMaxLength(256);
            e.HasIndex(x => new { x.TenantId, x.EkranKodu }).IsUnique();
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
            e.Property(x => x.Iban).HasMaxLength(34);     // roadmap K1 (IBAN max 34)
            e.Property(x => x.HesapNo).HasMaxLength(64);  // roadmap K1
            e.Property(x => x.Banka).HasMaxLength(128);   // roadmap K1
            e.Property(x => x.Sube).HasMaxLength(128);    // roadmap K1
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
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- PenaltyType / Ceza türü (tenant-owned; master sözlük) ----
        b.Entity<PenaltyType>(e =>
        {
            e.ToTable("CezaTurleri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.VarsayilanTutar).HasColumnType("numeric(19,4)");
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- KdvRate / KDV oranı (tenant-owned; master sözlük) ----
        b.Entity<KdvRate>(e =>
        {
            e.ToTable("KdvOranlari");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Oran).HasColumnType("numeric(9,4)");
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- VehicleGroup / Araç grubu (tenant-owned; tanım + fiyat-kural master) ----
        b.Entity<VehicleGroup>(e =>
        {
            e.ToTable("AracGruplari");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.Property(x => x.Sipp).HasMaxLength(8);
            e.Property(x => x.Segment).HasMaxLength(64);
            e.Property(x => x.KasaTuru).HasMaxLength(32);
            e.Property(x => x.Marka).HasMaxLength(64);
            e.Property(x => x.Tipi).HasMaxLength(64);
            e.Property(x => x.Provizyon).HasColumnType("numeric(19,4)");
            e.Property(x => x.Provizyon2).HasColumnType("numeric(19,4)");
            e.Property(x => x.MuafiyetTutari).HasColumnType("numeric(19,4)");
            e.Property(x => x.Muafiyet2).HasColumnType("numeric(19,4)");
            e.Property(x => x.AsimKmUcreti).HasColumnType("numeric(19,4)");
            e.Property(x => x.YakitFiyati).HasColumnType("numeric(19,4)");
            e.Property(x => x.SonraOdeOran).HasColumnType("numeric(9,4)");
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- RateMatrix / Tarife Matrisi (tenant-owned; fiyat-tanım, defter postalamaz) ----
        b.Entity<RateMatrix>(e =>
        {
            e.ToTable("TarifeMatris");
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK; çapraz-tenant referans imkansız)
            e.HasIndex(x => x.SubeId);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.Property(x => x.Kanal).HasMaxLength(64);
            e.Property(x => x.Sube).HasMaxLength(64);
            e.Property(x => x.Lokasyon).HasMaxLength(64);
            e.Property(x => x.AracGrupKod).HasMaxLength(32);
            e.Property(x => x.ParaBirimi).HasMaxLength(8);
            e.Property(x => x.Onaylayan).HasMaxLength(128);
            e.Property(x => x.OnayDurumu).HasConversion<int>();
            e.Property(x => x.Gun1).HasColumnType("numeric(19,4)");
            e.Property(x => x.Gun2).HasColumnType("numeric(19,4)");
            e.Property(x => x.Gun3).HasColumnType("numeric(19,4)");
            e.Property(x => x.Gun4).HasColumnType("numeric(19,4)");
            e.Property(x => x.Gun5).HasColumnType("numeric(19,4)");
            e.Property(x => x.Gun6).HasColumnType("numeric(19,4)");
            e.Property(x => x.Gun7).HasColumnType("numeric(19,4)");
            e.Property(x => x.MaxEsneklik).HasColumnType("numeric(9,4)");
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- CoverageProduct / Sigorta-Ek hizmet ürün kataloğu (tenant-owned; fiyat-tanım) ----
        b.Entity<CoverageProduct>(e =>
        {
            e.ToTable("SigortaUrunleri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.AdEn).HasMaxLength(128);
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.Property(x => x.Doviz).HasMaxLength(8);
            e.Property(x => x.Tur).HasConversion<int>();
            e.Property(x => x.GunlukUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- RentalRule / Kiralama kuralı-promosyon (tenant-owned; kural-tanım, defter postalamaz) ----
        b.Entity<RentalRule>(e =>
        {
            e.ToTable("KiralamaKurallari");
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK; çapraz-tenant referans imkansız)
            e.HasIndex(x => x.SubeId);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.Property(x => x.Kanal).HasMaxLength(64);
            e.Property(x => x.Sube).HasMaxLength(64);
            e.Property(x => x.AracGrupKod).HasMaxLength(32);
            e.Property(x => x.KampanyaKodu).HasMaxLength(64);
            e.Property(x => x.SartMetni).HasMaxLength(4000);
            e.Property(x => x.Iskonto).HasColumnType("numeric(9,4)");
            e.Property(x => x.SonraOdeOran).HasColumnType("numeric(9,4)");
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

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
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK; çapraz-tenant referans imkansız)
            e.HasIndex(x => x.SubeId);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Adres).HasMaxLength(512);
            e.Property(x => x.Telefon).HasMaxLength(32);
            // roadmap K3 derinlik
            e.Property(x => x.Eposta).HasMaxLength(128);
            e.Property(x => x.CalismaSaatleri).HasMaxLength(64);
            e.Property(x => x.TeslimUcreti).HasColumnType("numeric(19,4)");
            e.Property(x => x.Sube).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Vehicle (tenant-owned) ----
        b.Entity<Vehicle>(e =>
        {
            e.ToTable("Vehicles");
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK; çapraz-tenant referans imkansız)
            e.HasIndex(x => x.SubeId);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Plaka).IsRequired().HasMaxLength(16);
            e.Property(x => x.Marka).HasMaxLength(64);
            e.Property(x => x.Tip).HasMaxLength(64);
            e.Property(x => x.Grup).HasMaxLength(64);
            e.Property(x => x.Segment).HasMaxLength(64);
            e.Property(x => x.Sipp).HasMaxLength(8);
            e.Property(x => x.Renk).HasMaxLength(32);
            e.Property(x => x.SasiNo).HasMaxLength(32);
            e.Property(x => x.MotorNo).HasMaxLength(32);
            e.Property(x => x.Sube).HasMaxLength(64);
            e.Property(x => x.Durum).HasConversion<int>();
            e.Property(x => x.FiloDurum).HasConversion<int>();
            e.Property(x => x.Vites).HasConversion<int>();
            e.Property(x => x.Yakit).HasConversion<int>();
            // Parite zenginleştirme (additive)
            e.Property(x => x.RuhsatNo).HasMaxLength(32);
            e.Property(x => x.AracSahibi).HasMaxLength(128);
            e.Property(x => x.OzelKod1).HasMaxLength(64);
            e.Property(x => x.OzelKod2).HasMaxLength(64);
            e.Property(x => x.OzelKod3).HasMaxLength(64);
            e.Property(x => x.OzelKod4).HasMaxLength(64);
            e.Property(x => x.OzelKod5).HasMaxLength(64);
            e.Property(x => x.AlimBedeli).HasColumnType("numeric(19,4)");
            e.Property(x => x.AlisVergisiz).HasColumnType("numeric(19,4)");
            e.Property(x => x.AlisOtv).HasColumnType("numeric(19,4)");
            e.Property(x => x.AlisKdv).HasColumnType("numeric(19,4)");
            e.Property(x => x.AylikMaliyet).HasColumnType("numeric(19,4)");
            e.Property(x => x.FiloYonetimMaliyeti).HasColumnType("numeric(19,4)");
            e.Property(x => x.IkinciElDeger).HasColumnType("numeric(19,4)");
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
            e.Property(x => x.Gsm2).HasMaxLength(32);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Il).HasMaxLength(64);
            e.Property(x => x.Ilce).HasMaxLength(64);
            e.Property(x => x.Adres).HasMaxLength(512);
            e.Property(x => x.Kaynak).HasMaxLength(64);
            e.Property(x => x.MusteriTemsilcisi).HasMaxLength(128);
            e.Property(x => x.UyariNedeni).HasMaxLength(256);
            e.Property(x => x.EhliyetNo).HasMaxLength(32);
            e.Property(x => x.EhliyetSinifi).HasMaxLength(16);
            e.Property(x => x.EhliyetYeri).HasMaxLength(64);
            e.Property(x => x.Tarife).HasMaxLength(64);
            e.Property(x => x.RiskLimiti).HasColumnType("numeric(19,4)");
            e.Property(x => x.RiskMesaji).HasMaxLength(256);
            e.Property(x => x.HgsYansitmaTuru).HasMaxLength(32);
            // CRM parite zenginleştirme (additive)
            e.Property(x => x.Sinif).HasMaxLength(32);
            e.Property(x => x.BabaAdi).HasMaxLength(128);
            e.Property(x => x.AnaAdi).HasMaxLength(128);
            e.Property(x => x.PasaportNo).HasMaxLength(32);
            e.Property(x => x.FaturaDonemi).HasMaxLength(32);
            e.Property(x => x.TevkifatOrani).HasColumnType("numeric(9,4)");
            e.Property(x => x.Yetkili1Ad).HasMaxLength(128);
            e.Property(x => x.Yetkili1Tel).HasMaxLength(32);
            e.Property(x => x.Yetkili1Mail).HasMaxLength(256);
            e.Property(x => x.Yetkili2Ad).HasMaxLength(128);
            e.Property(x => x.Yetkili2Tel).HasMaxLength(32);
            e.Property(x => x.Yetkili2Mail).HasMaxLength(256);
            e.Property(x => x.Yetkili3Ad).HasMaxLength(128);
            e.Property(x => x.Yetkili3Tel).HasMaxLength(32);
            e.Property(x => x.Yetkili3Mail).HasMaxLength(256);
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
            e.Property(x => x.Provizyon).HasColumnType("numeric(19,4)");
            e.Property(x => x.Depozito).HasColumnType("numeric(19,4)");
            e.Property(x => x.KomisyonOran).HasColumnType("numeric(9,4)");
            e.Property(x => x.KomisyonTutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.DropUcreti).HasColumnType("numeric(19,4)");
            e.Property(x => x.SonraOdeOran).HasColumnType("numeric(9,4)");
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
            e.Property(x => x.Provizyon).HasColumnType("numeric(19,4)");
            e.Property(x => x.Depozito).HasColumnType("numeric(19,4)");
            e.Property(x => x.KomisyonOran).HasColumnType("numeric(9,4)");
            e.Property(x => x.KomisyonTutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.DropUcreti).HasColumnType("numeric(19,4)");
            e.Property(x => x.SonraOdeOran).HasColumnType("numeric(9,4)");
            e.Property(x => x.Aciklama).HasMaxLength(1024);
            e.HasIndex(x => new { x.TenantId, x.SozlesmeNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.VehicleId });
            e.HasQueryFilter(x => x.TenantId == TenantId);
            e.HasMany(x => x.EkHizmetler).WithOne().HasForeignKey(a => a.RentalId).OnDelete(DeleteBehavior.Cascade);
            // Double-booking exclusion constraint + generated Period kolonu migration'da (raw SQL).
        });

        // ---- RentalAddOn / Kira ek hizmet kalemi (tenant-owned; mutable, fatura öncesi) ----
        b.Entity<RentalAddOn>(e =>
        {
            e.ToTable("RentalAddOns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
            e.Property(x => x.Miktar).HasColumnType("numeric(19,4)");
            e.Property(x => x.BirimNetFiyat).HasColumnType("numeric(19,4)");
            e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
            e.Property(x => x.NetTutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.KdvTutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.Toplam).HasColumnType("numeric(19,4)");
            e.HasIndex(x => new { x.TenantId, x.RentalId });
            e.HasQueryFilter(x => x.TenantId == TenantId);
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
            e.Property(x => x.ZeyilPrim).HasColumnType("numeric(19,4)"); // roadmap J3
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
            e.Property(x => x.Ceza).HasColumnType("numeric(19,4)"); // roadmap J2
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

        // ---- FiloKiralama (uzun-dönem kira sözleşmesi; full-CRUD, mali belge DEĞİL → roadmap L1) ----
        b.Entity<FiloKiralama>(e =>
        {
            e.ToTable("FiloKiralamalar");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.No).IsRequired().HasMaxLength(32);
            e.Property(x => x.AylikUcret).HasColumnType("numeric(19,4)");
            e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
            e.Property(x => x.DamgaVergisi).HasColumnType("numeric(19,4)");
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.Property(x => x.Durum).HasConversion<int>();
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.MusteriId });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- AracSiparis (tedarik siparişi; full-CRUD, mali belge DEĞİL → roadmap L3) ----
        b.Entity<AracSiparis>(e =>
        {
            e.ToTable("AracSiparisleri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.No).IsRequired().HasMaxLength(32);
            e.Property(x => x.Tedarikci).IsRequired().HasMaxLength(200);
            e.Property(x => x.Marka).HasMaxLength(100);
            e.Property(x => x.Tip).HasMaxLength(100);
            e.Property(x => x.Grup).HasMaxLength(100);
            e.Property(x => x.BirimFiyat).HasColumnType("numeric(19,4)");
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.Property(x => x.Durum).HasConversion<int>();
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- AracKredi (banka kredisi takibi; full-CRUD, mali belge DEĞİL → roadmap L4) ----
        b.Entity<AracKredi>(e =>
        {
            e.ToTable("AracKredileri");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.No).IsRequired().HasMaxLength(32);
            e.Property(x => x.BankaAdi).IsRequired().HasMaxLength(200);
            e.Property(x => x.KrediTutari).HasColumnType("numeric(19,4)");
            e.Property(x => x.FaizOran).HasColumnType("numeric(9,4)");
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.Property(x => x.Durum).HasConversion<int>();
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Baf (personel araç tahsis; full-CRUD, mali belge DEĞİL → roadmap L5) ----
        b.Entity<Baf>(e =>
        {
            e.ToTable("Baflar");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.No).IsRequired().HasMaxLength(32);
            e.Property(x => x.Sube).HasMaxLength(100);
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.Property(x => x.Durum).HasConversion<int>();
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.PersonelId });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- HesapKodu / ServisTanim (basit Kod-master; full-CRUD → roadmap N1) ----
        b.Entity<HesapKodu>(e =>
        {
            e.ToTable("HesapKodlari");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.Ad).IsRequired().HasMaxLength(200);
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });
        b.Entity<ServisTanim>(e =>
        {
            e.ToTable("ServisTanimlari");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
            e.Property(x => x.AracTipi).IsRequired().HasMaxLength(100);
            e.Property(x => x.Aciklama).HasMaxLength(512);
            e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });
        b.Entity<DropTanim>(e =>
        {
            e.ToTable("DropTanimlari");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Lokasyon).IsRequired().HasMaxLength(150);
            e.Property(x => x.Sube).IsRequired().HasMaxLength(150);
            e.Property(x => x.KarsilamaSekli).HasMaxLength(100);
            e.Property(x => x.CalismaSekli).HasMaxLength(100);
            e.Property(x => x.OzelIletisim).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Lokasyon, x.Sube }).IsUnique();
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
            e.Property(x => x.YansitilanTutar).HasColumnType("numeric(19,4)"); // roadmap J4
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
            // Cari↔cari virman idempotency: işlem anahtarı (SourceId) verilince çift-submit yutulur.
            // Dengeli çift (hedef Borç / kaynak Alacak) farklı AccountRef'le ayrışır → ikisi de geçer;
            // aynı anahtarla tekrar gönderim çakışır (kısmi, yalnız 'CariVirman'). NOT: kolon kümesi
            // Hgs index'inden (…SourceId,Direction) FARKLI olmalı (…SourceId,AccountRef) ki EF iki ayrı
            // kısmi index üretsin, birini diğerinin yerine düşürmesin.
            e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.AccountRef })
                .IsUnique()
                .HasFilter("\"SourceType\" = 'CariVirman'")
                .HasDatabaseName("IX_AccountLedgerEntries_CariVirman_Idem");
            // Depozito idempotency (roadmap I3): aynı SourceId ile çift-submit yutulur. Dengeli çift
            // (Borç/Alacak) Direction'la ayrışır → ikisi de geçer; tekrar çakışır. Named overload ile
            // Hgs index'iyle (aynı kolon seti) ÇAKIŞMAYAN ayrı kısmi index üretilir.
            e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction }, "IX_AccountLedgerEntries_Depozito_Idem")
                .IsUnique()
                .HasFilter("\"SourceType\" LIKE 'Depozito%'");
            // MTV ödeme idempotency (roadmap J1): SourceId=mtvId; çift-ödeme reddedilir. Ayrı named kısmi index.
            e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction }, "IX_AccountLedgerEntries_MtvOdeme_Idem")
                .IsUnique()
                .HasFilter("\"SourceType\" = 'MtvOdeme'");
            // Muayene ödeme idempotency (roadmap J2): SourceId=inspectionId; çift-ödeme reddedilir.
            e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction }, "IX_AccountLedgerEntries_MuayeneOdeme_Idem")
                .IsUnique()
                .HasFilter("\"SourceType\" = 'MuayeneOdeme'");
            // Sigorta ödeme idempotency (roadmap J3): SourceId=policyId; çift-ödeme reddedilir.
            e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction }, "IX_AccountLedgerEntries_SigortaOdeme_Idem")
                .IsUnique()
                .HasFilter("\"SourceType\" = 'SigortaOdeme'");
            // Servis yansıtma/rücu idempotency (roadmap J4): SourceId=serviceId; çift-yansıtma reddedilir.
            e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction }, "IX_AccountLedgerEntries_ServisYansitma_Idem")
                .IsUnique()
                .HasFilter("\"SourceType\" = 'ServisYansitma'");
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
            // Toplu işlem idempotency: aynı IslemAnahtari iki kez yazılamaz (çift-submit batch'i geri alır).
            e.HasIndex(x => new { x.TenantId, x.IslemAnahtari })
                .IsUnique()
                .HasFilter("\"IslemAnahtari\" IS NOT NULL");
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
            // Vergi/belge alanları (parite #8; bilgi amaçlı, postlamaya yansımaz)
            e.Property(x => x.Otv).HasColumnType("numeric(19,4)");
            e.Property(x => x.TevkifatOran).HasColumnType("numeric(9,4)");
            e.Property(x => x.TevkifatTutar).HasColumnType("numeric(19,4)");
            e.Property(x => x.DamgaVergisi).HasColumnType("numeric(19,4)");
            e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CariId });
            // İdempotency: bir kira EN ÇOK bir kez faturalanır (RentalId dolu olduğunda benzersiz).
            e.HasIndex(x => new { x.TenantId, x.RentalId })
                .IsUnique()
                .HasFilter("\"RentalId\" IS NOT NULL");
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.InvoiceId);
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ---- Expense / Gider (tenant-owned; append-only + DB-immutable) ----
        b.Entity<Expense>(e =>
        {
            e.ToTable("Expenses");
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK; çapraz-tenant referans imkansız)
            e.HasIndex(x => x.SubeId);
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
            // Toplu gider idempotency: aynı IslemAnahtari iki kez yazılamaz (çift-submit batch'i geri alır).
            e.HasIndex(x => new { x.TenantId, x.IslemAnahtari })
                .IsUnique()
                .HasFilter("\"IslemAnahtari\" IS NOT NULL");
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
