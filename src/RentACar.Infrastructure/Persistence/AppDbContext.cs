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
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AccountLedgerEntry> AccountLedgerEntries => Set<AccountLedgerEntry>();

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

        // ---- AccountLedgerEntry (tenant-owned; PR #1'de iskelet) ----
        b.Entity<AccountLedgerEntry>(e =>
        {
            e.ToTable("AccountLedgerEntries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Direction).HasConversion<int>();
            e.Property(x => x.Description).HasMaxLength(512);
            // Money değer nesnesi: complex property (tutar + döviz + kur).
            e.ComplexProperty(x => x.Amount, m =>
            {
                m.Property(p => p.Amount).HasColumnName("Amount_Value").HasColumnType("numeric(19,4)");
                m.Property(p => p.Currency).HasColumnName("Amount_Currency").HasMaxLength(3);
                m.Property(p => p.Rate).HasColumnName("Amount_Rate").HasColumnType("numeric(19,6)");
            });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });
    }
}
