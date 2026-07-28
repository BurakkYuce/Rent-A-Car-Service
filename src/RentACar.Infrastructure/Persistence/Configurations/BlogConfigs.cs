using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- BlogPost (halka açık site blog yazısı, PR-6; tenant-owned, defter postalamaz) ----
internal sealed class BlogPostConfig : IEntityTypeConfiguration<BlogPost>
{
    public void Configure(EntityTypeBuilder<BlogPost> e)
    {
        e.ToTable("BlogYazilari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Baslik).IsRequired().HasMaxLength(200);
        e.Property(x => x.Slug).IsRequired().HasMaxLength(200);
        e.Property(x => x.Ozet).HasMaxLength(500);
        e.Property(x => x.Icerik).IsRequired().HasMaxLength(20000);
        e.Property(x => x.KapakContentType).HasMaxLength(32);
        e.Property(x => x.Durum).HasConversion<int>();
        // Slug URL'in kendisi → tenant içinde benzersiz (public /blog/{slug} tekil satır çözer).
        e.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
        // Public liste sorgusu: Durum=Yayinda + YayinTarihi DESC.
        e.HasIndex(x => new { x.TenantId, x.Durum, x.YayinTarihi });
    }
}
