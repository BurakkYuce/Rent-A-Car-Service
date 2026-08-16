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
        // SEO alanları — hepsi opsiyonel. Uzunluklar SAKLAMA sınırıdır, tavsiye sınırı DEĞİL:
        // arama motorunun kırpma eşiği (title ~60, description ~160) uygulamada UYARI olarak
        // gösterilir, veritabanı onu dayatmaz (yazarın metnini sessizce kesmek yanlış olurdu).
        e.Property(x => x.AltBaslik).HasMaxLength(300);
        e.Property(x => x.SeoBaslik).HasMaxLength(300);
        e.Property(x => x.MetaAciklama).HasMaxLength(500);
        e.Property(x => x.AnahtarKelimeler).HasMaxLength(500);
        e.Property(x => x.Yazar).HasMaxLength(160);
        e.Property(x => x.KapakAlt).HasMaxLength(300);
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
        e.Property(x => x.AtananAd).HasMaxLength(128); // PR-17: denormalize ad (Users'a join gerekmesin)
        // Staff kuyruğu: yeni talepler önce (Durum, tarih). PR-17'nin durum filtresi + sayfalaması
        // da bu index'i kullanıyor — YENİ index gerekmedi.
        e.HasIndex(x => new { x.TenantId, x.Durum, x.CreatedAtUtc });
    }
}

// ---- PR-16: halka açık site içerik sayfaları + SSS (tenant-owned; merkezi query filter kapsıyor) ----
internal sealed class SayfaIcerikConfig : IEntityTypeConfiguration<SayfaIcerik>
{
    public void Configure(EntityTypeBuilder<SayfaIcerik> e)
    {
        e.ToTable("SayfaIcerikler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Slug).IsRequired().HasMaxLength(200);
        e.Property(x => x.Baslik).IsRequired().HasMaxLength(200);
        e.Property(x => x.Govde).IsRequired().HasMaxLength(20_000);
        e.Property(x => x.MetaAciklama).HasMaxLength(300);
        // Kok seviyeli adres: tenant icinde benzersiz olmak ZORUNDA (iki sayfa ayni adresi paylasamaz).
        e.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
        // Footer/sitemap sorgusu Yayinda + Sira uzerinden gidiyor.
        e.HasIndex(x => new { x.TenantId, x.Yayinda, x.Sira });
    }
}

internal sealed class SssKaydiConfig : IEntityTypeConfiguration<SssKaydi>
{
    public void Configure(EntityTypeBuilder<SssKaydi> e)
    {
        e.ToTable("SssKayitlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Soru).IsRequired().HasMaxLength(300);
        e.Property(x => x.Cevap).IsRequired().HasMaxLength(4_000);
        e.HasIndex(x => new { x.TenantId, x.Yayinda, x.Sira });
    }
}

// ---- PR-17: talep takip notlari (tenant-owned) ----
internal sealed class TalepNotuConfig : IEntityTypeConfiguration<TalepNotu>
{
    public void Configure(EntityTypeBuilder<TalepNotu> e)
    {
        e.ToTable("TalepNotlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Metin).IsRequired().HasMaxLength(2_000);
        e.Property(x => x.Kullanici).HasMaxLength(128);
        // Talep silinmiyor; yine de butunluk icin composite FK (tenant-esli).
        e.HasOne<PublicBookingRequest>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.TalepId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.TenantId, x.TalepId, x.ZamanUtc });
    }
}
