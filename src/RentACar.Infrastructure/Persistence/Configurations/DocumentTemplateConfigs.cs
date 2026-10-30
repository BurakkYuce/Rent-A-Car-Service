using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- BelgeSablon (marka-özel PDF metin şablonu; tenant-owned master, defter postalamaz) ----
internal sealed class DocumentTemplateConfig : IEntityTypeConfiguration<BelgeSablon>
{
    public void Configure(EntityTypeBuilder<BelgeSablon> e)
    {
        e.ToTable("BelgeSablonlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.BelgeTuru).HasConversion<int>();
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.BelgeBasligi).HasMaxLength(256);
        e.Property(x => x.HukukiMetinSol).HasMaxLength(4000);
        e.Property(x => x.HukukiMetinSag).HasMaxLength(4000);
        e.Property(x => x.EkKosullarVarsayilan).HasMaxLength(4000);
        e.Property(x => x.AltBilgi).HasMaxLength(512);
        // Tür içinde şablon adı benzersiz (tenant başına).
        e.HasIndex(x => new { x.TenantId, x.BelgeTuru, x.Ad }).IsUnique();
    }
}
