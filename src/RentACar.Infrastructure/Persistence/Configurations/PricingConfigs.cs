// Fiyat/kural tanim tablolari (tarife, arac grubu, sigorta urunleri, kiralama kurallari).
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi
// dongu tum ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- RateCard / Tarife (tenant-owned; fiyat master) ----
internal sealed class RateCardConfig : IEntityTypeConfiguration<RateCard>
{
    public void Configure(EntityTypeBuilder<RateCard> e)
    {
        e.ToTable("RateCards");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Grup).IsRequired().HasMaxLength(64);
        e.Property(x => x.GunlukUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.Doviz).HasMaxLength(3);
        // Kod tenant içinde benzersiz (servis büyük harfe normalize eder).
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
        // Lookup: grup bazlı arama (büyük/küçük harf duyarsız ILike ile).
        e.HasIndex(x => new { x.TenantId, x.Grup });
    }
}

// ---- VehicleGroup / Araç grubu (tenant-owned; tanım + fiyat-kural master) ----
internal sealed class VehicleGroupConfig : IEntityTypeConfiguration<VehicleGroup>
{
    public void Configure(EntityTypeBuilder<VehicleGroup> e)
    {
        e.ToTable("AracGruplari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.Sipp).HasMaxLength(8);
        e.Property(x => x.Segment).HasMaxLength(64);
        e.Property(x => x.KasaTuru).HasMaxLength(32);
        e.Property(x => x.Marka).HasMaxLength(64);
        e.Property(x => x.Tipi).HasMaxLength(64);
        e.Property(x => x.Provizyon).HasColumnType("numeric(19,4)");
        e.Property(x => x.Provizyon2).HasColumnType("numeric(19,4)");
        e.Property(x => x.MuafiyetTutari).HasColumnType("numeric(19,4)");
        e.Property(x => x.Muafiyet2).HasColumnType("numeric(19,4)");
        e.Property(x => x.AsimKmUcreti).HasColumnType("numeric(19,4)");
        e.Property(x => x.YakitFiyati).HasColumnType("numeric(19,4)");
        e.Property(x => x.SonraOdeOran).HasColumnType("numeric(9,4)");
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- RateMatrix / Tarife Matrisi (tenant-owned; fiyat-tanım, defter postalamaz) ----
internal sealed class RateMatrixConfig : IEntityTypeConfiguration<RateMatrix>
{
    public void Configure(EntityTypeBuilder<RateMatrix> e)
    {
        e.ToTable("TarifeMatris");
        e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK; çapraz-tenant referans imkansız)
        e.HasIndex(x => x.SubeId);
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.Kanal).HasMaxLength(64);
        e.Property(x => x.Sube).HasMaxLength(64);
        e.Property(x => x.Lokasyon).HasMaxLength(64);
        e.Property(x => x.AracGrupKod).HasMaxLength(32);
        e.Property(x => x.ParaBirimi).HasMaxLength(8);
        e.Property(x => x.Onaylayan).HasMaxLength(128);
        e.Property(x => x.OnayDurumu).HasConversion<int>();
        e.Property(x => x.Gun1).HasColumnType("numeric(19,4)");
        e.Property(x => x.Gun2).HasColumnType("numeric(19,4)");
        e.Property(x => x.Gun3).HasColumnType("numeric(19,4)");
        e.Property(x => x.Gun4).HasColumnType("numeric(19,4)");
        e.Property(x => x.Gun5).HasColumnType("numeric(19,4)");
        e.Property(x => x.Gun6).HasColumnType("numeric(19,4)");
        e.Property(x => x.Gun7).HasColumnType("numeric(19,4)");
        e.Property(x => x.MaxEsneklik).HasColumnType("numeric(9,4)");
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- CoverageProduct / Sigorta-Ek hizmet ürün kataloğu (tenant-owned; fiyat-tanım) ----
internal sealed class CoverageProductConfig : IEntityTypeConfiguration<CoverageProduct>
{
    public void Configure(EntityTypeBuilder<CoverageProduct> e)
    {
        e.ToTable("SigortaUrunleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.AdEn).HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.Doviz).HasMaxLength(8);
        e.Property(x => x.Tur).HasConversion<int>();
        e.Property(x => x.GunlukUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- RentalRule / Kiralama kuralı-promosyon (tenant-owned; kural-tanım, defter postalamaz) ----
internal sealed class RentalRuleConfig : IEntityTypeConfiguration<RentalRule>
{
    public void Configure(EntityTypeBuilder<RentalRule> e)
    {
        e.ToTable("KiralamaKurallari");
        e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK; çapraz-tenant referans imkansız)
        e.HasIndex(x => x.SubeId);
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.Kanal).HasMaxLength(64);
        e.Property(x => x.Sube).HasMaxLength(64);
        e.Property(x => x.AracGrupKod).HasMaxLength(32);
        e.Property(x => x.KampanyaKodu).HasMaxLength(64);
        e.Property(x => x.SartMetni).HasMaxLength(4000);
        e.Property(x => x.Iskonto).HasColumnType("numeric(9,4)");
        e.Property(x => x.SonraOdeOran).HasColumnType("numeric(9,4)");
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- BrokerYasak / Broker-kaynak satış yasağı (tenant-owned; kural-tanım, defter postalamaz) ----
internal sealed class BrokerYasakConfig : IEntityTypeConfiguration<BrokerYasak>
{
    public void Configure(EntityTypeBuilder<BrokerYasak> e)
    {
        e.ToTable("BrokerYasaklari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.Kaynak).HasMaxLength(64);
        e.Property(x => x.AracGrupKod).HasMaxLength(32);
        e.Property(x => x.Bolge).HasMaxLength(64);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}
