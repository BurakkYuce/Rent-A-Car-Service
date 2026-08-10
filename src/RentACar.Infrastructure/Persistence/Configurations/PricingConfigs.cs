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
        // FAZ-72: tarife grubu referansı — composite tenant-FK (çapraz-tenant referans imkansız).
        //
        // DeleteBehavior.Restrict, SetNull DEĞİL: composite FK'de SET NULL, kolonların HEPSİNİ
        // (TenantId dahil) NULL'a çekmeye çalışır ve TenantId NOT NULL olduğu için silme 23502 ile
        // patlar. İstenen "bağ kopsun, satır kalsın" davranışı bu yüzden UYGULAMA tarafında,
        // silme ile AYNI transaction'da yapılıyor (TarifeGrubuRepository.DeleteAsync).
        e.HasOne<TarifeGrubu>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.TarifeGrubuId })
            .HasPrincipalKey(g => new { g.TenantId, g.Id })
            .OnDelete(DeleteBehavior.Restrict);
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
        // FAZ-20 sözlük derinliği
        e.Property(x => x.ProvizyonDoviz).HasMaxLength(3);
        e.Property(x => x.Provizyon2Doviz).HasMaxLength(3);
        e.Property(x => x.YakitTuru).HasConversion<int?>();
        e.Property(x => x.Vites).HasConversion<int?>();
        e.Property(x => x.EntegrasyonKod1).HasMaxLength(64);
        e.Property(x => x.WebId).HasMaxLength(64);
        e.Property(x => x.ServisId).HasMaxLength(64);
        e.Property(x => x.Provizyon).HasColumnType("numeric(19,4)");
        e.Property(x => x.Provizyon2).HasColumnType("numeric(19,4)");
        e.Property(x => x.MuafiyetTutari).HasColumnType("numeric(19,4)");
        e.Property(x => x.Muafiyet2).HasColumnType("numeric(19,4)");
        e.Property(x => x.AsimKmUcreti).HasColumnType("numeric(19,4)");
        e.Property(x => x.GencSurucuUcretGunluk).HasColumnType("numeric(19,4)"); // FAZ 3.A3a
        e.Property(x => x.EkSurucuUcretGunluk).HasColumnType("numeric(19,4)");
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
        e.Property(x => x.Turu).HasMaxLength(32);   // FAZ-70
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
        e.Property(x => x.GunHaftalik).HasColumnType("numeric(19,4)"); // FAZ 3.A1 uzun-dönem kademeleri
        e.Property(x => x.GunAylik).HasColumnType("numeric(19,4)");
        // FAZ-71 — kademe bazlı km aşım ücretleri (limitler int, kolon tipi varsayılan).
        e.Property(x => x.Km1Ucret).HasColumnType("numeric(19,4)");
        e.Property(x => x.Km2Ucret).HasColumnType("numeric(19,4)");
        e.Property(x => x.Km3Ucret).HasColumnType("numeric(19,4)");
        e.Property(x => x.Km4Ucret).HasColumnType("numeric(19,4)");
        e.Property(x => x.Km5Ucret).HasColumnType("numeric(19,4)");
        e.Property(x => x.Km6Ucret).HasColumnType("numeric(19,4)");
        e.Property(x => x.KmHaftalikUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.KmAylikUcret).HasColumnType("numeric(19,4)");
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
        e.Property(x => x.MusteriSegment).HasMaxLength(64); // FAZ 3.A2
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

// ---- DolulukFiyatKural (FAZ 3.A7 — doluluk-bazlı çarpan; tenant-owned master, defter postalamaz) ----
internal sealed class DolulukFiyatKuralConfig : IEntityTypeConfiguration<DolulukFiyatKural>
{
    public void Configure(EntityTypeBuilder<DolulukFiyatKural> e)
    {
        e.ToTable("DolulukFiyatKurallari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.AracGrupKod).HasMaxLength(32);
        e.Property(x => x.CarpanYuzde).HasColumnType("numeric(9,4)");
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- TarifeGrubu (FAZ-72 — fiyat grubu master; tenant-owned, defter postalamaz) ----
internal sealed class TarifeGrubuConfig : IEntityTypeConfiguration<TarifeGrubu>
{
    public void Configure(EntityTypeBuilder<TarifeGrubu> e)
    {
        e.ToTable("TarifeGruplari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Oran).HasColumnType("numeric(9,4)");
        e.Property(x => x.KullaniciAdi).HasMaxLength(128);
        e.Property(x => x.SifreHash).HasMaxLength(256);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- MaliyetTeklifi (FAZ-74 — kaydedilmiş filo maliyet teklifi) ----
// PLANLAMA BELGESİ, mali belge DEĞİL: deftere postalamaz → değişmezlik trigger'ı YOK, tam CRUD.
internal sealed class MaliyetTeklifiConfig : IEntityTypeConfiguration<MaliyetTeklifi>
{
    public void Configure(EntityTypeBuilder<MaliyetTeklifi> e)
    {
        e.ToTable("MaliyetTeklifleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();

        // Composite tenant-FK: çapraz-tenant referans yapısal olarak imkânsız.
        e.HasOne<Customer>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CariId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Personel>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HazirlayanId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        e.Property(x => x.KayitNo).IsRequired().HasMaxLength(32);
        e.Property(x => x.Baslik).IsRequired().HasMaxLength(256);
        e.Property(x => x.Plaka).HasMaxLength(32);
        e.Property(x => x.Aciklama).HasMaxLength(1024);
        e.Property(x => x.KrediHesaplamaSekli).HasConversion<int>();

        // Tutar/oran kolonlarının HEPSİ numeric(19,4) — tek tek yazmak yerine model üzerinden
        // dolaşılır (yeni kalem eklendiğinde kolon tipini yazmayı UNUTMA riski kalmaz).
        foreach (var p in e.Metadata.GetProperties()
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            p.SetColumnType("numeric(19,4)");

        e.Ignore(x => x.FiloToplamMaliyet);      // TÜRETİLMİŞ — kolon değil
        e.Ignore(x => x.FiloTeklifAylikNet);
        e.Ignore(x => x.FiloTeklifKdvli);

        e.HasIndex(x => new { x.TenantId, x.KayitNo }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.Tarih });
        e.HasIndex(x => new { x.TenantId, x.CariId });
    }
}
