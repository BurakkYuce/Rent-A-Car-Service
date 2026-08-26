// Tenant ayarlari, bildirim/WhatsApp loglari, ekran yetki override.
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi
// dongu tum ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- TenantSettings / Ayarlar (tenant-owned; tenant başına TEK satır, roadmap D1) ----
// Sır alanları (*Enc) ŞİFRELİ cipher saklar (servis ISecretProtector ile); kolon düz metin değildir.
internal sealed class TenantSettingsConfig : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> e)
    {
        e.ToTable("Ayarlar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.FirmaUnvan).HasMaxLength(256);
        e.Property(x => x.FirmaVergiDairesi).HasMaxLength(128);
        e.Property(x => x.FirmaVergiNo).HasMaxLength(32);
        e.Property(x => x.FirmaAdres).HasMaxLength(512);
        e.Property(x => x.FirmaTel).HasMaxLength(64);
        e.Property(x => x.FirmaEmail).HasMaxLength(128);
        e.Property(x => x.FirmaMobilTel).HasMaxLength(64);
        e.Property(x => x.FirmaMarka).HasMaxLength(128);
        // FAZ-81 renk kodları: "#rrggbb" → 7 karakter
        e.Property(x => x.RenkGecikenler).HasMaxLength(7);
        e.Property(x => x.RenkBugunDonecekler).HasMaxLength(7);
        e.Property(x => x.RenkBugunCikacaklar).HasMaxLength(7);
        e.Property(x => x.RenkOpsiyonlu).HasMaxLength(7);
        e.Property(x => x.RenkLimitBakiye).HasMaxLength(7);
        e.Property(x => x.RenkAlacakli).HasMaxLength(7);
        e.Property(x => x.RenkRezAtananPlaka).HasMaxLength(7);
        e.Property(x => x.RenkKiralanmayan).HasMaxLength(7);
        e.Property(x => x.EFaturaKullanici).HasMaxLength(128);
        e.Property(x => x.EFaturaSifreEnc).HasMaxLength(1024);
        e.Property(x => x.SmsBaslik).HasMaxLength(64);
        e.Property(x => x.SmsApiKeyEnc).HasMaxLength(1024);
        e.Property(x => x.PosMerchantId).HasMaxLength(128);
        e.Property(x => x.PosApiKeyEnc).HasMaxLength(1024);
        // roadmap M1 derinlik
        e.Property(x => x.LogoUrl).HasMaxLength(512);
        e.Property(x => x.VarsayilanDoviz).HasMaxLength(3);
        e.Property(x => x.VarsayilanKdvOrani).HasColumnType("numeric(5,4)");
        // FAZ-82: fiyat türü SERBEST metin değil, sabit listeden gelen bir etiket (servis doğruluyor) —
        // kolon uzunluğu en uzun seçeneğe ("KDV Dahil Günlük") rahat yeten 32 karakterle sınırlı.
        e.Property(x => x.VarsayilanFiyatTuru).HasMaxLength(32);
        e.Property(x => x.SmtpHost).HasMaxLength(256);
        e.Property(x => x.SmtpKullanici).HasMaxLength(256);
        e.Property(x => x.SmtpSifreEnc).HasMaxLength(1024);
        e.Property(x => x.SmtpGonderenAdres).HasMaxLength(256);
        e.Property(x => x.SmtpGonderenAd).HasMaxLength(128);
        e.Property(x => x.FaturaSeriKodu).HasMaxLength(3);   // GİB: tam 3 karakter
        e.Property(x => x.WhatsAppNumarasi).HasMaxLength(32);
        // PR-10: varsayılan araç grubu. Grup silinirse ayar NULL'a düşer (ON DELETE SET NULL) —
        // ölü Id'ye işaret eden ayar, çözücüde "bulunamadı" olarak sessizce Ekonomi'ye kayardı.
        // Tenant-arası bütünlük: FK tek kolonlu (Id) ama çözücü grubu AKTİF olarak arar ve o okuma
        // query-filter + RLS altındadır → başka tenant'ın Id'si yazılsa bile çözülmez (null döner).
        e.HasOne<VehicleGroup>().WithMany()
            .HasForeignKey(x => x.VarsayilanGrupId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => x.TenantId).IsUnique(); // tenant başına tek satır
    }
}

// ---- Bildirim / uygulama-içi vade uyarısı (tenant-owned; scheduler yazar) ----
internal sealed class BildirimConfig : IEntityTypeConfiguration<Bildirim>
{
    public void Configure(EntityTypeBuilder<Bildirim> e)
    {
        e.ToTable("Bildirimler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Tur).IsRequired().HasMaxLength(16);
        e.Property(x => x.Mesaj).IsRequired().HasMaxLength(256);
        // İdempotency: aynı kaynak (Tur+araç+vade) için tek bildirim.
        e.HasIndex(x => new { x.TenantId, x.Tur, x.VehicleId, x.VadeTarihi }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.Okundu });
    }
}

// ---- WhatsAppGonderim (tenant-owned; günlük özet log/idempotency) ----
internal sealed class WhatsAppGonderimConfig : IEntityTypeConfiguration<WhatsAppGonderim>
{
    public void Configure(EntityTypeBuilder<WhatsAppGonderim> e)
    {
        e.ToTable("WhatsAppGonderimler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Tur).IsRequired().HasMaxLength(16);
        e.Property(x => x.Alici).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ozet).HasMaxLength(1024);
        e.Property(x => x.HataMesaji).HasMaxLength(1024);
        e.HasIndex(x => new { x.TenantId, x.Gun, x.Tur }).IsUnique(); // günde tek gönderim
    }
}

// ---- MesajSablon (tenant-owned; müşteriye giden mesaj metni — tür + kanal başına tek) ----
internal sealed class MesajSablonConfig : IEntityTypeConfiguration<MesajSablon>
{
    public void Configure(EntityTypeBuilder<MesajSablon> e)
    {
        e.ToTable("MesajSablonlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Tur).HasConversion<string>().IsRequired().HasMaxLength(32);
        e.Property(x => x.Kanal).HasConversion<string>().IsRequired().HasMaxLength(16);
        e.Property(x => x.Konu).HasMaxLength(256);
        e.Property(x => x.Govde).IsRequired().HasMaxLength(8192);
        // Tür + kanal başına TEK şablon: "hangi metin gitti" sorusunun tek cevabı olsun.
        e.HasIndex(x => new { x.TenantId, x.Tur, x.Kanal }).IsUnique();
    }
}

// ---- GidenMesaj (tenant-owned; gönderim kaydı + ŞEMA düzeyinde idempotency) ----
internal sealed class GidenMesajConfig : IEntityTypeConfiguration<GidenMesaj>
{
    public void Configure(EntityTypeBuilder<GidenMesaj> e)
    {
        e.ToTable("GidenMesajlar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Anahtar).IsRequired().HasMaxLength(128);
        e.Property(x => x.Tur).IsRequired().HasMaxLength(32);
        e.Property(x => x.Kanal).HasConversion<string>().IsRequired().HasMaxLength(16);
        e.Property(x => x.Alici).IsRequired().HasMaxLength(256);
        e.Property(x => x.Konu).HasMaxLength(256);
        e.Property(x => x.Govde).IsRequired().HasMaxLength(8192);
        e.Property(x => x.DegerlerJson).HasMaxLength(4096);
        e.Property(x => x.Durum).HasConversion<string>().IsRequired().HasMaxLength(16);
        e.Property(x => x.Hata).HasMaxLength(1024);
        e.Property(x => x.KaynakTur).HasMaxLength(32);
        // İDEMPOTENCY ŞEMADA: aynı anahtar tenant içinde iki kez yazılamaz. Job iki kez koşsa da
        // (çoklu instance, elle tetikleme) müşteri aynı mesajı iki kez almaz — uygulama katmanının
        // "önce sorgula, sonra yaz" kontrolü yarışta yetersiz kalır, benzersiz index kalmaz.
        e.HasIndex(x => new { x.TenantId, x.Anahtar }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.Durum, x.OlusturmaUtc });
    }
}

// ---- ScreenPermission / Ekran yetki override (tenant-owned, roadmap E3) ----
internal sealed class ScreenPermissionConfig : IEntityTypeConfiguration<ScreenPermission>
{
    public void Configure(EntityTypeBuilder<ScreenPermission> e)
    {
        e.ToTable("EkranYetkileri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.EkranKodu).IsRequired().HasMaxLength(64);
        e.Property(x => x.AllowedRolesCsv).HasMaxLength(256);
        e.HasIndex(x => new { x.TenantId, x.EkranKodu }).IsUnique();
    }
}

// ---- YetkiGrup / ekran-izni şablonu (tenant-owned, PR-D) ----
internal sealed class YetkiGrupConfig : IEntityTypeConfiguration<YetkiGrup>
{
    public void Configure(EntityTypeBuilder<YetkiGrup> e)
    {
        e.ToTable("YetkiGruplari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.KalemlerJson).IsRequired().HasMaxLength(8000);
        e.HasIndex(x => new { x.TenantId, x.Ad }).IsUnique();
    }
}
