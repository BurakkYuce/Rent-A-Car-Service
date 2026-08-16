using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- FirmaDokuman (firmanın KENDİ yüklediği PDF'ler; tenant-owned, defter postalamaz) ----
// HasQueryFilter YAZILMAZ: OnModelCreating'deki merkezi ITenantOwned döngüsü uygular (ModelGuardTests doğrular).
internal sealed class FirmaDokumanConfig : IEntityTypeConfiguration<FirmaDokuman>
{
    public void Configure(EntityTypeBuilder<FirmaDokuman> e)
    {
        e.ToTable("FirmaDokumanlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Baslik).IsRequired().HasMaxLength(200);
        e.Property(x => x.Aciklama).HasMaxLength(1000);
        e.Property(x => x.DosyaAdi).IsRequired().HasMaxLength(120);
        e.Property(x => x.ContentType).IsRequired().HasMaxLength(64);
        e.Property(x => x.YukleyenKullanici).HasMaxLength(128);

        // Yuva (Sira) tenant içinde BENZERSİZ. Migration'daki CHECK (1..10) ile birlikte
        // "tenant başına en fazla 10 doküman" sınırını YAPISAL yapar: servisteki sayım
        // yarışta atlansa bile 11. satır DB'ye giremez.
        e.HasIndex(x => new { x.TenantId, x.Sira }).IsUnique();
    }
}
