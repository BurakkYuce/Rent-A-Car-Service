using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

/// <summary>Filo plan hedefi (FAZ-19). Yeni dikey → ayrı config dosyası.</summary>
internal sealed class FiloPlanHedefiConfig : IEntityTypeConfiguration<FiloPlanHedefi>
{
    public void Configure(EntityTypeBuilder<FiloPlanHedefi> e)
    {
        e.ToTable("FiloPlanHedefleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.AracGrupAdi).HasMaxLength(64);
        e.Property(x => x.Sipp).HasMaxLength(16);
        e.Property(x => x.Donem).HasMaxLength(32);
        e.Property(x => x.Aciklama).HasMaxLength(512);

        // Doğal anahtar migration'da ELLE kuruluyor: NULLS NOT DISTINCT gerekiyor. Üç alanın da
        // nullable olması yüzünden PG varsayılanında (grup, NULL, NULL) ikinci kez yazılabilir ve
        // aynı hedef iki kez tanımlanabilirdi.
        e.HasIndex(x => new { x.TenantId, x.AracGrupAdi, x.Sipp, x.Donem })
            .IsUnique()
            .HasDatabaseName("IX_FiloPlanHedefleri_Tenant_Grup_Sipp_Donem");
    }
}
