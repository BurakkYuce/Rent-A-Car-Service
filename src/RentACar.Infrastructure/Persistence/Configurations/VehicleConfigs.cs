// Arac + filo tablolari (satis, hasar, filo kiralama, siparis, kredi, BAF).
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi
// dongu tum ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- Vehicle (tenant-owned) ----
internal sealed class VehicleConfig : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> e)
    {
        e.ToTable("Vehicles");
        e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK; çapraz-tenant referans imkansız)
        e.HasIndex(x => x.SubeId);
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Plaka).IsRequired().HasMaxLength(16);
        e.Property(x => x.Marka).HasMaxLength(64);
        e.Property(x => x.Tip).HasMaxLength(64);
        e.Property(x => x.Grup).HasMaxLength(64);
        e.Property(x => x.Segment).HasMaxLength(64);
        e.Property(x => x.Sipp).HasMaxLength(8);
        e.Property(x => x.Renk).HasMaxLength(32);
        e.Property(x => x.SasiNo).HasMaxLength(32);
        e.Property(x => x.MotorNo).HasMaxLength(32);
        e.Property(x => x.Sube).HasMaxLength(64);
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.FiloDurum).HasConversion<int>();
        e.Property(x => x.Vites).HasConversion<int>();
        e.Property(x => x.Yakit).HasConversion<int>();
        // Parite zenginleştirme (additive)
        e.Property(x => x.RuhsatNo).HasMaxLength(32);
        e.Property(x => x.AracSahibi).HasMaxLength(128);
        e.Property(x => x.OzelKod1).HasMaxLength(64);
        e.Property(x => x.OzelKod2).HasMaxLength(64);
        e.Property(x => x.OzelKod3).HasMaxLength(64);
        e.Property(x => x.OzelKod4).HasMaxLength(64);
        e.Property(x => x.OzelKod5).HasMaxLength(64);
        e.Property(x => x.AlimBedeli).HasColumnType("numeric(19,4)");
        e.Property(x => x.AlisVergisiz).HasColumnType("numeric(19,4)");
        e.Property(x => x.AlisOtv).HasColumnType("numeric(19,4)");
        e.Property(x => x.AlisKdv).HasColumnType("numeric(19,4)");
        e.Property(x => x.AylikMaliyet).HasColumnType("numeric(19,4)");
        e.Property(x => x.FiloYonetimMaliyeti).HasColumnType("numeric(19,4)");
        e.Property(x => x.IkinciElDeger).HasColumnType("numeric(19,4)");
        // Plaka tenant içinde benzersiz (doğal iş anahtarı).
        e.HasIndex(x => new { x.TenantId, x.Plaka }).IsUnique();
    }
}

// ---- VehicleSale / Araç Satış (tenant-owned; mali belge → DB-immutable) ----
internal sealed class VehicleSaleConfig : IEntityTypeConfiguration<VehicleSale>
{
    public void Configure(EntityTypeBuilder<VehicleSale> e)
    {
        e.ToTable("VehicleSales");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.NoterNo).HasMaxLength(64);
        e.Property(x => x.SatisNet).HasColumnType("numeric(19,4)");
        e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
        e.Property(x => x.KdvTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.GenelToplam).HasColumnType("numeric(19,4)");
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.Durum).HasConversion<int>();
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.AliciCariId });
        // Bir araç EN FAZLA bir kez 'Tamamlandi' satılabilir (çift satış güvencesi).
        e.HasIndex(x => new { x.TenantId, x.VehicleId })
            .IsUnique()
            .HasFilter("\"Durum\" = 0");
    }
}

// ---- DamageFile / BAF (tenant-owned; onay akışı, mali belge DEĞİL → güncellenebilir) ----
internal sealed class DamageFileConfig : IEntityTypeConfiguration<DamageFile>
{
    public void Configure(EntityTypeBuilder<DamageFile> e)
    {
        e.ToTable("DamageFiles");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.Aciklama).HasMaxLength(1024);
        e.Property(x => x.TahminiTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.OnayNotu).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
        e.HasIndex(x => new { x.TenantId, x.Durum });
    }
}

// ---- FiloKiralama (uzun-dönem kira sözleşmesi; full-CRUD, mali belge DEĞİL → roadmap L1) ----
internal sealed class FiloKiralamaConfig : IEntityTypeConfiguration<FiloKiralama>
{
    public void Configure(EntityTypeBuilder<FiloKiralama> e)
    {
        e.ToTable("FiloKiralamalar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.AylikUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
        e.Property(x => x.DamgaVergisi).HasColumnType("numeric(19,4)");
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.Durum).HasConversion<int>();
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.MusteriId });
    }
}

// ---- AracSiparis (tedarik siparişi; full-CRUD, mali belge DEĞİL → roadmap L3) ----
internal sealed class AracSiparisConfig : IEntityTypeConfiguration<AracSiparis>
{
    public void Configure(EntityTypeBuilder<AracSiparis> e)
    {
        e.ToTable("AracSiparisleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.Tedarikci).IsRequired().HasMaxLength(200);
        e.Property(x => x.Marka).HasMaxLength(100);
        e.Property(x => x.Tip).HasMaxLength(100);
        e.Property(x => x.Grup).HasMaxLength(100);
        e.Property(x => x.BirimFiyat).HasColumnType("numeric(19,4)");
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.Durum).HasConversion<int>();
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
    }
}

// ---- AracKredi (banka kredisi takibi; full-CRUD, mali belge DEĞİL → roadmap L4) ----
internal sealed class AracKrediConfig : IEntityTypeConfiguration<AracKredi>
{
    public void Configure(EntityTypeBuilder<AracKredi> e)
    {
        e.ToTable("AracKredileri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.BankaAdi).IsRequired().HasMaxLength(200);
        e.Property(x => x.KrediTutari).HasColumnType("numeric(19,4)");
        e.Property(x => x.FaizOran).HasColumnType("numeric(9,4)");
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.Durum).HasConversion<int>();
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
    }
}

// ---- Baf (personel araç tahsis; full-CRUD, mali belge DEĞİL → roadmap L5) ----
internal sealed class BafConfig : IEntityTypeConfiguration<Baf>
{
    public void Configure(EntityTypeBuilder<Baf> e)
    {
        e.ToTable("Baflar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.Sube).HasMaxLength(100);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.Durum).HasConversion<int>();
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.PersonelId });
    }
}

// ---- VehicleKmLog (FAZ 2.5 — km zaman serisi; salt-ekleme, mali belge değil) ----
internal sealed class VehicleKmLogConfig : IEntityTypeConfiguration<VehicleKmLog>
{
    public void Configure(EntityTypeBuilder<VehicleKmLog> e)
    {
        e.ToTable("VehicleKmLoglari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kaynak).HasConversion<int>();
        e.HasIndex(x => new { x.TenantId, x.VehicleId, x.Tarih });
    }
}

// ---- VehiclePhoto (PR-3 — halka açık site araç galerisi) ----
internal sealed class VehiclePhotoConfig : IEntityTypeConfiguration<VehiclePhoto>
{
    public void Configure(EntityTypeBuilder<VehiclePhoto> e)
    {
        e.ToTable("VehiclePhotos");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.ContentType).IsRequired().HasMaxLength(32);
        // Nav'sız FK — UserConfig'in Branch FK'sı gibi. CustomerContact'tan FARKLI: vehicle silinince
        // fotoğraflar da cascade düşsün (yetim bytea kalmasın) — VehicleRepository.DeleteAsync gerçek
        // hard-delete, soft-delete YOK, cascade güvenle çalışır.
        e.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Cascade);
        // BİLİNÇLİ NON-UNIQUE: MoveAsync iki satırın Sira'sını TAKAS eder (iki ayrı UPDATE) — unique
        // olsaydı ilk UPDATE'ten sonra iki satır aynı Sira'yı taşır, constraint patlardı.
        e.HasIndex(x => new { x.TenantId, x.VehicleId, x.Sira });
    }
}
