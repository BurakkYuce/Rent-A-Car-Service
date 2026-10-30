using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

/// <summary>Assistans (yol yardım) talebi — FAZ-44. Yeni dikey olduğu için ayrı config dosyası.</summary>
internal sealed class AssistanceRequestConfig : IEntityTypeConfiguration<AssistansTalep>
{
    public void Configure(EntityTypeBuilder<AssistansTalep> e)
    {
        e.ToTable("AssistansTalepleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();

        // Sözleşme bağı OPSİYONEL; composite tenant-FK ile çapraz-tenant referans imkânsız.
        // Restrict: sözleşme silinse bile olay tutanağı kaybolmasın (zaten snapshot taşıyor).
        e.HasOne<RentalContract>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RentalId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);

        e.Property(x => x.Plaka).HasMaxLength(16);
        e.Property(x => x.AdSoyad).HasMaxLength(256);
        e.Property(x => x.CepTel).HasMaxLength(32);
        e.Property(x => x.Mesaj).IsRequired().HasMaxLength(2048);
        e.Property(x => x.Sebep).HasMaxLength(512);
        e.Property(x => x.Cozum).HasMaxLength(1024);

        e.HasIndex(x => new { x.TenantId, x.Plaka });   // plaka araması
        e.HasIndex(x => new { x.TenantId, x.Zaman });   // tarih aralığı
        e.HasIndex(x => new { x.TenantId, x.RentalId });
    }
}
