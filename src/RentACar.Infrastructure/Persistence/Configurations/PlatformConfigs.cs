// Platform tablolari (RLS YOK; Tenants/Users/TenantDomains). Tenant filtresi UYGULANMAZ.
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi
// dongu tum ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- Tenant (platform) ----
internal sealed class TenantConfig : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> e)
    {
        e.ToTable("Tenants");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Code).IsRequired().HasMaxLength(64);
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Name).IsRequired().HasMaxLength(256);
        // Konsol v2 bilgi alanları (bilgi amaçlı; Kapanis semantiği Tenant.cs'te).
        e.Property(x => x.YetkiliAd).HasMaxLength(128);
        e.Property(x => x.Eposta).HasMaxLength(256);
        e.Property(x => x.Telefon).HasMaxLength(32);
        e.Property(x => x.Notlar).HasMaxLength(2000);
        e.Property(x => x.Plan).HasMaxLength(64);
    }
}

// ---- User (platform; tenant'a bağlı ama RLS yok) ----
internal sealed class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> e)
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
        e.Property(x => x.CalendarToken).HasMaxLength(64);
        e.HasIndex(x => x.CalendarToken).IsUnique(); // token→user çözümü (null'lar Postgres'te çakışmaz)
    }
}

// ---- TenantDomain (platform; public site host→tenant eşlemesi, PR-0) ----
internal sealed class TenantDomainConfig : IEntityTypeConfiguration<TenantDomain>
{
    public void Configure(EntityTypeBuilder<TenantDomain> e)
    {
        e.ToTable("TenantDomains");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Host).IsRequired().HasMaxLength(256);
        e.HasIndex(x => x.Host).IsUnique();
        e.Property(x => x.Kind).HasConversion<int>();
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.VerificationToken).HasMaxLength(128);
        e.HasIndex(x => x.TenantId);
    }
}

// ---- PlatformBelge / PlatformBelgeHedef (PR-B — platformdan tenant'a PDF dağıtımı) ----
// PLATFORM tabloları: ITenantOwned DEĞİL → merkezi tenant-filtre döngüsü dokunmaz, RLS de yok.
// İzolasyon uygulama katmanında (PlatformBelgeRepository.Gorunur — dört koşul).
internal sealed class PlatformBelgeConfig : IEntityTypeConfiguration<PlatformBelge>
{
    public void Configure(EntityTypeBuilder<PlatformBelge> e)
    {
        e.ToTable("PlatformBelgeler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Baslik).IsRequired().HasMaxLength(200);
        e.Property(x => x.Aciklama).HasMaxLength(1000);
        e.Property(x => x.DosyaAdi).IsRequired().HasMaxLength(120);
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.YukleyenOperator).HasMaxLength(128);
        // Liste sorgusu Durum + tarih üzerinden gidiyor.
        e.HasIndex(x => new { x.Durum, x.GuncellemeUtc });
    }
}

internal sealed class PlatformBelgeHedefConfig : IEntityTypeConfiguration<PlatformBelgeHedef>
{
    public void Configure(EntityTypeBuilder<PlatformBelgeHedef> e)
    {
        e.ToTable("PlatformBelgeHedefler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        // Belge silinince hedefleri de düşer (yetim satır kalmasın).
        e.HasOne<PlatformBelge>().WithMany().HasForeignKey(x => x.BelgeId).OnDelete(DeleteBehavior.Cascade);
        // Tenant silinmiyor (kapatılıyor) ama bütünlük için FK + Restrict.
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        // Aynı belge aynı tenant'a iki kez hedeflenmesin.
        e.HasIndex(x => new { x.BelgeId, x.TenantId }).IsUnique();
    }
}
