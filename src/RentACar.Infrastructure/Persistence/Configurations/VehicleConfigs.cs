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
        // PR-13: halka açık site ilanı üyeliği. Composite FK (şube deseniyle aynı) → çapraz-tenant
        // referans imkânsız. SET NULL: ilan silinince araç yayından düşer, araç kaydı bozulmaz.
        e.HasOne<WebIlan>().WithMany().HasForeignKey(x => new { x.TenantId, x.WebIlanId })
            .HasPrincipalKey(i => new { i.TenantId, i.Id }).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => new { x.TenantId, x.WebIlanId });
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
        // FAZ-28 detay alanları
        e.Property(x => x.RuhsatSahibi).HasMaxLength(128);
        e.Property(x => x.SozNo).HasMaxLength(64);
        e.Property(x => x.AraciAlan).HasMaxLength(128);
        e.Property(x => x.Kiralayan).HasMaxLength(128);
        e.Property(x => x.AssistanFirma).HasMaxLength(128);
        e.Property(x => x.TsbKodu).HasMaxLength(32);
        e.Property(x => x.OdemeSekli).HasMaxLength(64);
        e.Property(x => x.PasifSebep).HasMaxLength(256);
        e.Property(x => x.SonDurum).HasMaxLength(256);
        e.Property(x => x.HgsFirma).HasMaxLength(128);
        e.Property(x => x.KiraFiyat).HasColumnType("numeric(19,4)");
        e.Property(x => x.TsbKaskoDegeri).HasColumnType("numeric(19,4)");
        e.Property(x => x.AlisEuroFiyat).HasColumnType("numeric(19,4)");
        e.Property(x => x.SatisEuroFiyat).HasColumnType("numeric(19,4)");
        e.Property(x => x.AlimBedeli).HasColumnType("numeric(19,4)");
        e.Property(x => x.AlisVergisiz).HasColumnType("numeric(19,4)");
        e.Property(x => x.AlisOtv).HasColumnType("numeric(19,4)");
        e.Property(x => x.AlisKdv).HasColumnType("numeric(19,4)");
        e.Property(x => x.AylikMaliyet).HasColumnType("numeric(19,4)");
        e.Property(x => x.FiloYonetimMaliyeti).HasColumnType("numeric(19,4)");
        e.Property(x => x.IkinciElDeger).HasColumnType("numeric(19,4)");
        // FAZ-10 araç kartı derinliği (hepsi bilgi alanı — hesaba girmez)
        e.Property(x => x.TsrbMarkaKodu).HasMaxLength(32);
        e.Property(x => x.TsrbTipKodu).HasMaxLength(32);
        e.Property(x => x.AltGrupAdi).HasMaxLength(64);
        e.Property(x => x.EntegrasyonKodu).HasMaxLength(64);
        e.Property(x => x.TeypKodu).HasMaxLength(64);
        e.Property(x => x.TakipMarka).HasMaxLength(64);
        e.Property(x => x.TakipNo).HasMaxLength(64);
        e.Property(x => x.SahipGrup).HasMaxLength(64);
        e.Property(x => x.AracSahibiNo).HasMaxLength(64);
        e.Property(x => x.AracSahibi2).HasMaxLength(128);
        e.Property(x => x.KrediFirma).HasMaxLength(128);
        e.Property(x => x.Konum).HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(1024);
        // Kur alanları numeric(19,4) — TUTAR değil KUR taşırlar ve hiçbir hesaba girmezler.
        e.Property(x => x.AlimBedeliKur).HasColumnType("numeric(19,4)");
        e.Property(x => x.Arac2FiyatKur).HasColumnType("numeric(19,4)");
        e.Property(x => x.SimdiKur).HasColumnType("numeric(19,4)");
        e.Property(x => x.AylikMaliyetDoviz).HasColumnType("numeric(19,4)");
        // Plaka tenant içinde benzersiz (doğal iş anahtarı).
        e.HasIndex(x => new { x.TenantId, x.Plaka }).IsUnique();
        // FAZ-75 belge takibi
        e.Property(x => x.Kimde).HasMaxLength(128);

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
        e.Property(x => x.IhaleFirmasi).HasMaxLength(128);   // FAZ-28
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
        // FAZ-21 sözleşme meta-alanları
        e.Property(x => x.SatisTemsilcisi).HasMaxLength(128);
        e.Property(x => x.FaturaTuru).HasMaxLength(32);
        e.Property(x => x.MakbuzNo).HasMaxLength(32);
        e.Property(x => x.DosyaNo).HasMaxLength(32);
        e.Property(x => x.SozlesmeNo).HasMaxLength(32);
        e.Property(x => x.FiyatTuru).HasMaxLength(32);
        e.Property(x => x.Kaynak).HasMaxLength(64);
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.MusteriId });
        e.HasIndex(x => new { x.TenantId, x.BasTar });   // FAZ-21 tarih filtresi
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

        // ---- FAZ-17 derinlik ----
        e.Property(x => x.DosyaNo).HasMaxLength(64);
        e.Property(x => x.SatisTemsilci).HasMaxLength(128);
        e.Property(x => x.OzelTemsilci).HasMaxLength(128);
        e.Property(x => x.Versiyon).HasMaxLength(100);
        e.Property(x => x.Opsiyon).HasMaxLength(512);
        e.Property(x => x.Renk).HasMaxLength(64);
        e.Property(x => x.IcRenk).HasMaxLength(64);
        e.Property(x => x.KaynakTip).HasMaxLength(32);
        e.Property(x => x.SatisTipi).HasMaxLength(32);
        e.Property(x => x.TsbKayitNo).HasMaxLength(64);
        // Üç fiyat katmanı SALT BİLGİ; nullable — "girilmemiş" ile "0" ayrı anlam taşır.
        e.Property(x => x.PiyasaFiyat).HasColumnType("numeric(19,4)");
        e.Property(x => x.OpsFiyat).HasColumnType("numeric(19,4)");
        e.Property(x => x.FiloFiyat).HasColumnType("numeric(19,4)");

        // Cari bağı — composite tenant-FK (AracKredi/FAZ-13 deseni): çapraz-tenant referans YAPISAL
        // olarak imkânsız (tek kolonlu FK, RLS'in altından başka tenant'ın Id'sine bağlanmayı teknik
        // olarak engellemezdi). Restrict: bağlı sipariş varken cari silinemez.
        e.HasOne<Customer>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.TedarikciCariId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Kredi bağı — aynı composite tenant-FK deseni. Restrict: siparişe bağlı kredi silinemez.
        e.HasOne<AracKredi>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.KrediId })
            .HasPrincipalKey(k => new { k.TenantId, k.Id })
            .OnDelete(DeleteBehavior.Restrict);

        e.HasIndex(x => new { x.TenantId, x.TedarikciCariId });
        e.HasIndex(x => new { x.TenantId, x.DosyaNo });
        e.HasIndex(x => new { x.TenantId, x.SiparisTarihi });   // FAZ-17 tarih aralığı filtresi
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

        // FAZ-13 — Cari bağı. Composite tenant-FK: çapraz-tenant referans YAPISAL olarak imkânsız
        // (tek kolonlu FK, RLS'in altından başka tenant'ın Id'sine bağlanmayı teknik olarak
        // engellemezdi). Restrict: bağlı kredi varken cari silinemez.
        e.HasOne<Customer>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CariId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.DosyaNo).HasMaxLength(64);
        e.HasIndex(x => new { x.TenantId, x.CariId });
        e.HasIndex(x => new { x.TenantId, x.DosyaNo });

        // FAZ-17 — AracSiparis.KrediId composite tenant-FK'sinin hedefi (TenantId, Id): tenant
        // sınırını FK'nin KENDİSİ taşır, başka tenant'ın kredisine sipariş bağlanamaz.
        e.HasAlternateKey(x => new { x.TenantId, x.Id });
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

// ---- WebIlan / WebIlanOzellik (PR-13 — halka açık site ilanları) ----
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi döngü tüm
// ITenantOwned entity'lere tenant filtresini otomatik uygular (ModelGuardTests bunu doğrular).
internal sealed class WebIlanConfig : IEntityTypeConfiguration<WebIlan>
{
    public void Configure(EntityTypeBuilder<WebIlan> e)
    {
        e.ToTable("WebIlanlar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Baslik).IsRequired().HasMaxLength(160);
        e.Property(x => x.Slug).IsRequired().HasMaxLength(200);
        // Slug URL'in kendisi → tenant içinde benzersiz (public /araclar/{slug} tekil satır çözer).
        e.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
        e.Property(x => x.EslesmeAnahtari).HasMaxLength(256);
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.GunlukFiyat).HasColumnType("numeric(19,4)");
        e.Property(x => x.HaftalikToplam).HasColumnType("numeric(19,4)");
        e.Property(x => x.AylikToplam).HasColumnType("numeric(19,4)");
        // Composite alternatif anahtar: Vehicle.WebIlanId'nin (TenantId, WebIlanId) FK'si buna bağlanır.
        e.HasAlternateKey(x => new { x.TenantId, x.Id });
        // EslesmeAnahtari UNIQUE DEĞİL — "ayrı göster" modu aynı anahtardan bilinçli N ilan üretir.
        e.HasIndex(x => new { x.TenantId, x.EslesmeAnahtari });
        e.HasIndex(x => new { x.TenantId, x.Durum, x.Sira });
    }
}

internal sealed class WebIlanOzellikConfig : IEntityTypeConfiguration<WebIlanOzellik>
{
    public void Configure(EntityTypeBuilder<WebIlanOzellik> e)
    {
        e.ToTable("WebIlanOzellikler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Etiket).IsRequired().HasMaxLength(60);
        e.Property(x => x.Deger).IsRequired().HasMaxLength(160);
        // İlan silinince özellikleri de düşer (yetim satır kalmasın) — composite FK ile çapraz-tenant kapalı.
        e.HasOne<WebIlan>().WithMany().HasForeignKey(x => new { x.TenantId, x.IlanId })
            .HasPrincipalKey(i => new { i.TenantId, i.Id }).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.TenantId, x.IlanId, x.Sira });
    }
}
