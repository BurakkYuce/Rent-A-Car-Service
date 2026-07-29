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

// ---- PublicBookingRequest (halka açık site rezervasyon talebi/lead, PR-8) ----
internal sealed class PublicBookingRequestConfig : IEntityTypeConfiguration<PublicBookingRequest>
{
    public void Configure(EntityTypeBuilder<PublicBookingRequest> e)
    {
        e.ToTable("SiteTalepleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.AdSoyad).IsRequired().HasMaxLength(160);
        e.Property(x => x.Telefon).IsRequired().HasMaxLength(32);
        e.Property(x => x.Email).HasMaxLength(160);
        e.Property(x => x.AracGrupKod).HasMaxLength(32);
        e.Property(x => x.Sube).HasMaxLength(128);
        e.Property(x => x.Not).HasMaxLength(2000); // sınırsız serbest metin = ucuz depolama şişirmesi
        e.Property(x => x.GosterilenGunlukUcretKdvDahil).HasColumnType("numeric(19,4)");
        e.Property(x => x.Durum).HasConversion<int>();
        // Staff kuyruğu: yeni talepler önce (Durum, tarih).
        e.HasIndex(x => new { x.TenantId, x.Durum, x.CreatedAtUtc });
    }
}
