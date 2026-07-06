// Kur tablolari: KurKaydi (PLATFORM, paylasimli, RLS YOK) + SabitKur (tenant-owned).
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi
// dongu tum ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- KurKaydi (PLATFORM; TCMB günlük kur — paylaşımlı, RLS YOK, TenantId YOK; Tenants deseni) ----
internal sealed class KurKaydiConfig : IEntityTypeConfiguration<KurKaydi>
{
    public void Configure(EntityTypeBuilder<KurKaydi> e)
    {
        e.ToTable("KurKayitlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(3);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.ForexAlis).HasColumnType("numeric(19,6)");
        e.Property(x => x.ForexSatis).HasColumnType("numeric(19,6)");
        e.Property(x => x.EfektifAlis).HasColumnType("numeric(19,6)");
        e.Property(x => x.EfektifSatis).HasColumnType("numeric(19,6)");
        e.HasIndex(x => new { x.Tarih, x.Kod }).IsUnique();
        // RLS YOK, HasQueryFilter YOK — ulusal/paylaşımlı veri.
    }
}

// ---- SabitKur (tenant-owned; kur sabitleme — RLS) ----
internal sealed class SabitKurConfig : IEntityTypeConfiguration<SabitKur>
{
    public void Configure(EntityTypeBuilder<SabitKur> e)
    {
        e.ToTable("SabitKurlar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(3);
        e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}
