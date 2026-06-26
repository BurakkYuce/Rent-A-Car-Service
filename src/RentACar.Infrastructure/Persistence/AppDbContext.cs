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
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<RentalContract> Rentals => Set<RentalContract>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AccountLedgerEntry> AccountLedgerEntries => Set<AccountLedgerEntry>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

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
            e.HasIndex(x => new { x.TenantId, x.UserName }).IsUnique();
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
