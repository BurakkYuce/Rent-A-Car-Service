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
        e.Property(x => x.SmtpHost).HasMaxLength(256);
        e.Property(x => x.SmtpKullanici).HasMaxLength(256);
        e.Property(x => x.SmtpSifreEnc).HasMaxLength(1024);
        e.Property(x => x.WhatsAppNumarasi).HasMaxLength(32);
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
