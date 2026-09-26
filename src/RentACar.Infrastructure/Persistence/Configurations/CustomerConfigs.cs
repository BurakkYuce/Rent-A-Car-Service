// Cari/CRM + personel/hukuk tablolari.
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi
// dongu tum ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- Customer / Cari (tenant-owned) ----
internal sealed class CustomerConfig : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> e)
    {
        e.ToTable("Customers");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Tip).HasConversion<int>();
        e.Property(x => x.Ad).HasMaxLength(128);
        e.Property(x => x.Soyad).HasMaxLength(128);
        e.Property(x => x.TcKimlik).HasMaxLength(11);        // ESKİ düz metin — backfill null'lar
        // KVKK/F2: PII cipher + blind-index kolonları
        e.Property(x => x.TcKimlikEnc).HasMaxLength(1024);
        e.Property(x => x.TcKimlikHash).HasMaxLength(64);    // HMAC-SHA256 hex
        e.Property(x => x.EhliyetNoEnc).HasMaxLength(1024);
        e.Property(x => x.PasaportNoEnc).HasMaxLength(1024);
        e.Property(x => x.Unvan).HasMaxLength(256);
        e.Property(x => x.VergiDairesi).HasMaxLength(128);
        e.Property(x => x.VergiNo).HasMaxLength(16);
        e.Property(x => x.CepTel).HasMaxLength(32);
        e.Property(x => x.Gsm2).HasMaxLength(32);
        e.Property(x => x.Email).HasMaxLength(256);
        e.Property(x => x.Il).HasMaxLength(64);
        e.Property(x => x.Ilce).HasMaxLength(64);
        e.Property(x => x.Adres).HasMaxLength(512);
        e.Property(x => x.Kaynak).HasMaxLength(64);
        e.Property(x => x.MusteriTemsilcisi).HasMaxLength(128);
        e.Property(x => x.UyariNedeni).HasMaxLength(256);
        e.Property(x => x.EhliyetNo).HasMaxLength(32);
        e.Property(x => x.EhliyetSinifi).HasMaxLength(16);
        e.Property(x => x.EhliyetYeri).HasMaxLength(64);
        e.Property(x => x.Tarife).HasMaxLength(64);
        e.Property(x => x.RiskLimiti).HasColumnType("numeric(19,4)");
        e.Property(x => x.RiskMesaji).HasMaxLength(256);
        e.Property(x => x.HgsYansitmaTuru).HasMaxLength(32);
        // CRM parite zenginleştirme (additive)
        e.Property(x => x.Sinif).HasMaxLength(32);
        e.Property(x => x.BabaAdi).HasMaxLength(128);
        e.Property(x => x.AnaAdi).HasMaxLength(128);
        e.Property(x => x.PasaportNo).HasMaxLength(32);
        e.Property(x => x.FaturaDonemi).HasMaxLength(32);
        e.Property(x => x.TevkifatOrani).HasColumnType("numeric(9,4)");
        e.Property(x => x.Yetkili1Ad).HasMaxLength(128);
        e.Property(x => x.Yetkili1Tel).HasMaxLength(32);
        e.Property(x => x.Yetkili1Mail).HasMaxLength(256);
        e.Property(x => x.Yetkili2Ad).HasMaxLength(128);
        e.Property(x => x.Yetkili2Tel).HasMaxLength(32);
        e.Property(x => x.Yetkili2Mail).HasMaxLength(256);
        e.Property(x => x.Yetkili3Ad).HasMaxLength(128);
        e.Property(x => x.Yetkili3Tel).HasMaxLength(32);
        e.Property(x => x.Yetkili3Mail).HasMaxLength(256);
        // roadmap K4
        e.Property(x => x.EkAdres).HasMaxLength(512);
        e.Property(x => x.BankaIban).HasMaxLength(34);
        e.Property(x => x.BankaAdi).HasMaxLength(128);
        e.Property(x => x.FaturaAdresi).HasMaxLength(512);
        e.Property(x => x.FaturaUnvan).HasMaxLength(256);
        e.Ignore(x => x.DisplayName);
        // Tenant içinde benzersiz — yalnız dolu olduğunda (kısmi unique index).
        // KVKK/F2: TC benzersizliği düz metin yerine blind-index üzerinde (şifreli PII'da
        // deterministik eşleşme anahtarı). Eski (TenantId, TcKimlik) indexi migration'da düşürüldü.
        e.HasIndex(x => new { x.TenantId, x.TcKimlikHash })
            .IsUnique()
            .HasFilter("\"TcKimlikHash\" IS NOT NULL");
        // PR-E: kurumsal yetkili kişileri child (cascade — Customer silinince düşer; ServiceRecord/ServiceLine deseni).
        e.HasMany(x => x.Kisiler).WithOne().HasForeignKey(k => k.MusteriId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.TenantId, x.VergiNo })
            .IsUnique()
            .HasFilter("\"VergiNo\" IS NOT NULL");
        // ---- FAZ-40 derinlik ----
        e.Property(x => x.Ulke).HasMaxLength(64);
        e.Property(x => x.Tel2).HasMaxLength(32);
        e.Property(x => x.OzelKod).HasMaxLength(64);
        e.Property(x => x.EntegrasyonKodu).HasMaxLength(64);
        e.Property(x => x.Aciklama).HasMaxLength(1024);
        e.Property(x => x.RiskIzin).HasMaxLength(64);
        e.Property(x => x.BayiKomisyon).HasColumnType("numeric(19,4)");
        e.Property(x => x.DogumYeri).HasMaxLength(128);
        e.Property(x => x.PasaportYeri).HasMaxLength(128);
        e.Property(x => x.KurumsalNo).HasMaxLength(64);
        e.Property(x => x.SifreHash).HasMaxLength(256);
        e.Property(x => x.UyariSerbest).HasMaxLength(512);
        e.Property(x => x.WebIndirim).HasColumnType("numeric(19,4)");
        e.Property(x => x.TevkifatKodu).HasMaxLength(32);
        e.Property(x => x.FaturaKiralayanIsim).HasMaxLength(256);
        e.Property(x => x.IsAdresi).HasMaxLength(512);
        e.Property(x => x.IsTelefonu).HasMaxLength(32);
        e.Property(x => x.KayitliIl).HasMaxLength(64);
        e.Property(x => x.KayitliIlce).HasMaxLength(64);
        e.Property(x => x.MahalleKoy).HasMaxLength(128);
        e.Property(x => x.SeriNo).HasMaxLength(32);
        e.Property(x => x.CiltNo).HasMaxLength(32);
        e.Property(x => x.AileSira).HasMaxLength(32);
        e.Property(x => x.SiraNo).HasMaxLength(32);
        e.HasIndex(x => new { x.TenantId, x.EntegrasyonKodu });
        e.HasIndex(x => new { x.TenantId, x.FirmaId });

    }
}

// ---- Personel (tenant-owned; master, roadmap C1) ----
// PII (*Enc) ŞİFRELİ cipher saklar (servis ISecretProtector ile); kolon düz metin değildir.
// ---- CustomerContact / Kurumsal cari yetkili kişisi (tenant-owned child, PR-E) ----
internal sealed class CustomerContactConfig : IEntityTypeConfiguration<CustomerContact>
{
    public void Configure(EntityTypeBuilder<CustomerContact> e)
    {
        e.ToTable("CariYetkiliKisiler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.AdSoyad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Telefon).HasMaxLength(32);
        e.Property(x => x.Mail).HasMaxLength(256);
        e.Property(x => x.Gorev).HasMaxLength(64);
        e.HasIndex(x => new { x.TenantId, x.MusteriId });
    }
}

internal sealed class PersonnelConfig : IEntityTypeConfiguration<Personel>
{
    public void Configure(EntityTypeBuilder<Personel> e)
    {
        e.ToTable("Personeller");
        e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK; çapraz-tenant referans imkansız)
        e.HasIndex(x => x.SubeId);
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Soyad).IsRequired().HasMaxLength(128);
        e.Property(x => x.TcKimlikEnc).HasMaxLength(1024);
        e.Property(x => x.SurucuBelgeNo).HasMaxLength(64);
        e.Property(x => x.MaasEnc).HasMaxLength(1024);
        e.Property(x => x.Sube).HasMaxLength(128);
        // FAZ-40 derinlik
        e.Property(x => x.GorevTanimi).HasMaxLength(64);
        e.Property(x => x.Adres).HasMaxLength(512);
        e.Property(x => x.EvTelefonu).HasMaxLength(32);
        e.Property(x => x.IsTelefonu).HasMaxLength(32);
        e.Property(x => x.CepTel).HasMaxLength(32);
        e.Property(x => x.MailAdresi).HasMaxLength(128);
        e.Property(x => x.Referans).HasMaxLength(256);
        e.Property(x => x.Aciklama).HasMaxLength(1024);
        e.Property(x => x.SSinifi).HasMaxLength(16);
        e.Property(x => x.SVerilisYeri).HasMaxLength(128);
        e.Property(x => x.DogumYeri).HasMaxLength(128);
        e.Property(x => x.BabaAdi).HasMaxLength(128);
        e.Property(x => x.AnaAdi).HasMaxLength(128);
        e.Property(x => x.Il).HasMaxLength(64);
        e.Property(x => x.Ilce).HasMaxLength(64);
        e.Property(x => x.Mahalle).HasMaxLength(128);
        e.Property(x => x.CiltNo).HasMaxLength(32);
        e.Property(x => x.AileSiraNo).HasMaxLength(32);
        e.Property(x => x.SiraNo).HasMaxLength(32);
        e.Property(x => x.KanGrubu).HasMaxLength(8);
        e.Property(x => x.RacTabletNo).HasMaxLength(32);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.GorevTanimi });
    }
}

// ---- HukukDosya (tenant-owned; master, roadmap C2) ----
internal sealed class LegalCaseConfig : IEntityTypeConfiguration<HukukDosya>
{
    public void Configure(EntityTypeBuilder<HukukDosya> e)
    {
        e.ToTable("HukukDosyalari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.DosyaNo).IsRequired().HasMaxLength(64);
        e.Property(x => x.Avukat).HasMaxLength(128);
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.Tur).HasConversion<int>();
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.Aciklama).HasMaxLength(1024);
        // FAZ-41 derinlik (additive, nullable)
        e.Property(x => x.FaturaNoTemp).HasMaxLength(64);
        e.Property(x => x.AvukatTel).HasMaxLength(32);
        e.Property(x => x.AvukatMail).HasMaxLength(256);
        e.Property(x => x.Avukat2Ad).HasMaxLength(128);
        e.Property(x => x.Avukat2Tel).HasMaxLength(32);
        e.Property(x => x.Avukat2Mail).HasMaxLength(256);
        e.Property(x => x.Tahsilat).HasColumnType("numeric(19,4)");
        // Kalan TÜRETİLMİŞ (Tutar-Tahsilat) — kolon açmıyoruz ki iki kaynak çelişemesin.
        e.Ignore(x => x.Kalan);
        e.HasIndex(x => new { x.TenantId, x.DosyaNo }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.FaturaNoTemp }); // liste "Fatura No" araması
        e.HasIndex(x => new { x.TenantId, x.CariId });       // liste "Ad Soyad" (cari) süzgeci
    }
}

// ---- Anket / Sikayet (tenant-owned; CRM, roadmap C3) ----
internal sealed class SurveyConfig : IEntityTypeConfiguration<Anket>
{
    public void Configure(EntityTypeBuilder<Anket> e)
    {
        e.ToTable("Anketler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Yorum).HasMaxLength(1024);
        e.Property(x => x.Kaynak).HasMaxLength(64);
        e.HasIndex(x => new { x.TenantId, x.Tarih });
        // FAZ-42
        e.Property(x => x.AnketTuru).HasConversion<int?>();
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.CikisOfisi).HasMaxLength(128);
        e.HasOne<RentalContract>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RentalId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);   // sözleşme silinse bile anket kaybolmaz
        e.HasIndex(x => new { x.TenantId, x.RentalId });
        e.HasIndex(x => new { x.TenantId, x.Tarih });

    }
}

internal sealed class ComplaintConfig : IEntityTypeConfiguration<Sikayet>
{
    public void Configure(EntityTypeBuilder<Sikayet> e)
    {
        e.ToTable("Sikayetler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Konu).IsRequired().HasMaxLength(256);
        e.Property(x => x.Detay).HasMaxLength(2048);
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.Cozum).HasMaxLength(2048);
        // FAZ-43 teslim/dönüş bağı
        e.Property(x => x.SikayetKanali).HasMaxLength(64);
        e.Property(x => x.SikayetYeri).HasConversion<int?>();
        e.Property(x => x.CikisOfisi).HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Tarih });
        e.HasIndex(x => new { x.TenantId, x.RentalId });
    }
}

// ---- RezSart (FAZ-25 — müşteri özel talebi/şartı; tenant-owned, para taşımaz) ----
internal sealed class ReservationTermConfig : IEntityTypeConfiguration<RezSart>
{
    public void Configure(EntityTypeBuilder<RezSart> e)
    {
        e.ToTable("RezSartlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Sart).IsRequired().HasMaxLength(512);
        e.Property(x => x.Grup).HasMaxLength(64);
        e.Property(x => x.TeslimEden).HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.MusteriId });
        // Durum filtresi (karşılandı/karşılanmadı) bu kolondan türetiliyor → indeksli.
        e.HasIndex(x => new { x.TenantId, x.KarsilamaTarihi });
        e.HasIndex(x => new { x.TenantId, x.TalepTarihi });
    }
}

/// <summary>Anket cevabı (FAZ-42) — anketin child satırı; soru metni snapshot.</summary>
internal sealed class SurveyAnswerConfig : IEntityTypeConfiguration<AnketCevap>
{
    public void Configure(EntityTypeBuilder<AnketCevap> e)
    {
        e.ToTable("AnketCevaplari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.HasOne<Anket>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AnketId })
            .HasPrincipalKey(a => new { a.TenantId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);   // anket silinince cevapları da gider (child)
        e.Property(x => x.Soru).IsRequired().HasMaxLength(512);
        e.Property(x => x.Cevap).HasMaxLength(1024);
        e.Property(x => x.Aciklama).HasMaxLength(1024);
        // Aynı ankette aynı soru sırası iki kez olamaz.
        e.HasIndex(x => new { x.TenantId, x.AnketId, x.SoruNo }).IsUnique();
    }
}
