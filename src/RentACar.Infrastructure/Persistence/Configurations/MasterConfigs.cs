// Master sozlukler (Kod+Ad tanim tablolari; tenant-owned).
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi
// dongu tum ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- Branch / Şube (tenant-owned; master sözlük) ----
internal sealed class BranchConfig : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> e)
    {
        e.ToTable("Branches");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Adres).HasMaxLength(512);
        e.Property(x => x.Telefon).HasMaxLength(32);
        // roadmap K3 derinlik
        e.Property(x => x.Eposta).HasMaxLength(128);
        e.Property(x => x.Il).HasMaxLength(64);
        e.Property(x => x.Ilce).HasMaxLength(64);
        // FAZ-23 şube derinliği
        e.Property(x => x.WebIsim).HasMaxLength(128);
        e.Property(x => x.FirmaUnvani).HasMaxLength(256);
        e.Property(x => x.RezervasyonRengi).HasMaxLength(7);
        e.Property(x => x.WebOtoparkId).HasMaxLength(64);
        e.Property(x => x.BayiCariKod).HasMaxLength(64);
        e.Property(x => x.BayiOfisId).HasMaxLength(64);
        e.Property(x => x.KomisyonHesabi).HasMaxLength(32);
        e.Property(x => x.OnlineRezId).HasMaxLength(64);
        e.Property(x => x.SozlesmeNoFormati).HasMaxLength(64);
        e.Property(x => x.EntegrasyonKodu).HasMaxLength(64);
        e.Property(x => x.ResimDosyasi).HasMaxLength(512);
        e.Property(x => x.HaftalikCalismaSaatleri).HasMaxLength(1024);
        e.Property(x => x.Enlem).HasColumnType("numeric(9,6)");
        e.Property(x => x.Boylam).HasColumnType("numeric(9,6)");
        e.Property(x => x.HizmetKomisyonOran).HasColumnType("numeric(9,4)");
        e.Property(x => x.Yetkili).HasMaxLength(128);
        e.Property(x => x.CalismaSaatleri).HasMaxLength(64);
        e.Property(x => x.KomisyonOran).HasColumnType("numeric(5,4)");
        e.Property(x => x.EvrakNoOnek).HasMaxLength(16);
        // Kod tenant içinde benzersiz (doğal iş anahtarı; servis büyük harfe normalize eder).
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- Location / Ofis (tenant-owned; master sözlük) ----
internal sealed class LocationConfig : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> e)
    {
        e.ToTable("Locations");
        e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK; çapraz-tenant referans imkansız)
        e.HasIndex(x => x.SubeId);
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Adres).HasMaxLength(512);
        e.Property(x => x.Telefon).HasMaxLength(32);
        // roadmap K3 derinlik
        e.Property(x => x.Eposta).HasMaxLength(128);
        e.Property(x => x.CalismaSaatleri).HasMaxLength(64);
        e.Property(x => x.TeslimUcreti).HasColumnType("numeric(19,4)");
        e.Property(x => x.Sube).HasMaxLength(64);
        // FAZ-22 derinlik
        e.Property(x => x.IngilizceAd).HasMaxLength(128);
        e.Property(x => x.BulusmaNoktasi).HasMaxLength(128);
        e.Property(x => x.Iata).HasMaxLength(8);
        e.Property(x => x.LokasyonTuru).HasMaxLength(64);
        e.Property(x => x.BinaNo).HasMaxLength(32);
        e.Property(x => x.Tarif).HasMaxLength(1024);
        e.Property(x => x.Ulke).HasMaxLength(64);
        e.Property(x => x.PostaKodu).HasMaxLength(16);
        e.Property(x => x.MapsKonumu).HasMaxLength(256);
        e.Property(x => x.EkAciklama).HasMaxLength(1024);
        e.Property(x => x.DropKarsilamaTuru).HasMaxLength(64);
        e.Property(x => x.DropCalismaSekli).HasMaxLength(64);
        e.Property(x => x.OzelMail).HasMaxLength(128);
        e.Property(x => x.OzelTelefon).HasMaxLength(32);

        // Haftalık saatler JSONB. ValueComparer ŞART: koleksiyon referansı değişmediğinde EF
        // içeriği "değişmedi" sayar ve düzenleme SESSİZCE kaydedilmez (spec'in uyardığı tuzak).
        e.Property(x => x.HaftalikCalismaSaatleri)
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<GunSaat>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<GunSaat>(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<GunSaat>>(
                    (a, b) => System.Text.Json.JsonSerializer.Serialize(a, (System.Text.Json.JsonSerializerOptions?)null)
                           == System.Text.Json.JsonSerializer.Serialize(b, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null).GetHashCode(),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<GunSaat>>(
                        System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                        (System.Text.Json.JsonSerializerOptions?)null) ?? new List<GunSaat>()));

        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.Iata });
    }
}

// ---- FuelKind / Yakıt türü (tenant-owned; master sözlük) ----
internal sealed class FuelKindConfig : IEntityTypeConfiguration<FuelKind>
{
    public void Configure(EntityTypeBuilder<FuelKind> e)
    {
        e.ToTable("YakitTurleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- TransmissionType / Vites türü (tenant-owned; master sözlük) ----
internal sealed class TransmissionTypeConfig : IEntityTypeConfiguration<TransmissionType>
{
    public void Configure(EntityTypeBuilder<TransmissionType> e)
    {
        e.ToTable("VitesTurleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- VehicleColor / Renk (tenant-owned; master sözlük) ----
internal sealed class VehicleColorConfig : IEntityTypeConfiguration<VehicleColor>
{
    public void Configure(EntityTypeBuilder<VehicleColor> e)
    {
        e.ToTable("Renkler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- CustomerGroup / Müşteri grubu (tenant-owned; master sözlük) ----
internal sealed class CustomerGroupConfig : IEntityTypeConfiguration<CustomerGroup>
{
    public void Configure(EntityTypeBuilder<CustomerGroup> e)
    {
        e.ToTable("MusteriGruplari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- InsuranceCompany / Sigorta şirketi (tenant-owned; master sözlük) ----
internal sealed class InsuranceCompanyConfig : IEntityTypeConfiguration<InsuranceCompany>
{
    public void Configure(EntityTypeBuilder<InsuranceCompany> e)
    {
        e.ToTable("SigortaSirketleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Telefon).HasMaxLength(32);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- Bank / Banka (tenant-owned; master sözlük) ----
internal sealed class BankConfig : IEntityTypeConfiguration<Bank>
{
    public void Configure(EntityTypeBuilder<Bank> e)
    {
        e.ToTable("Bankalar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- Department / Departman (tenant-owned; master sözlük) ----
internal sealed class DepartmentConfig : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> e)
    {
        e.ToTable("Departmanlar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- PaymentType / Ödeme tipi (tenant-owned; master sözlük) ----
internal sealed class PaymentTypeConfig : IEntityTypeConfiguration<PaymentType>
{
    public void Configure(EntityTypeBuilder<PaymentType> e)
    {
        e.ToTable("OdemeTipleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- Country / Ülke (tenant-owned; master sözlük) ----
internal sealed class CountryConfig : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> e)
    {
        e.ToTable("Ulkeler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- Accessory / Aksesuar (tenant-owned; master sözlük) ----
internal sealed class AccessoryConfig : IEntityTypeConfiguration<Accessory>
{
    public void Configure(EntityTypeBuilder<Accessory> e)
    {
        e.ToTable("Aksesuarlar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- EkHizmetTanim / Ek hizmet tanımı (tenant-owned; master sözlük) ----
internal sealed class EkHizmetTanimConfig : IEntityTypeConfiguration<EkHizmetTanim>
{
    public void Configure(EntityTypeBuilder<EkHizmetTanim> e)
    {
        e.ToTable("EkHizmetTanimlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.BirimUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
        e.Property(x => x.Aciklama).HasMaxLength(512);   // FAZ-80
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- Brand / Marka (tenant-owned; master sözlük) ----
internal sealed class BrandConfig : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> e)
    {
        e.ToTable("Markalar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- PenaltyType / Ceza türü (tenant-owned; master sözlük) ----
internal sealed class PenaltyTypeConfig : IEntityTypeConfiguration<PenaltyType>
{
    public void Configure(EntityTypeBuilder<PenaltyType> e)
    {
        e.ToTable("CezaTurleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.VarsayilanTutar).HasColumnType("numeric(19,4)");
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- KdvRate / KDV oranı (tenant-owned; master sözlük) ----
internal sealed class KdvRateConfig : IEntityTypeConfiguration<KdvRate>
{
    public void Configure(EntityTypeBuilder<KdvRate> e)
    {
        e.ToTable("KdvOranlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Oran).HasColumnType("numeric(9,4)");
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- CancelReason / İptal sebebi (tenant-owned; master sözlük) ----
internal sealed class CancelReasonConfig : IEntityTypeConfiguration<CancelReason>
{
    public void Configure(EntityTypeBuilder<CancelReason> e)
    {
        e.ToTable("IptalSebepleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- ReservationSource / Rezervasyon kaynağı (tenant-owned; master sözlük) ----
internal sealed class ReservationSourceConfig : IEntityTypeConfiguration<ReservationSource>
{
    public void Configure(EntityTypeBuilder<ReservationSource> e)
    {
        e.ToTable("RezervasyonKaynaklari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- VehicleSegment / Araç segment (tenant-owned; master sözlük) ----
internal sealed class VehicleSegmentConfig : IEntityTypeConfiguration<VehicleSegment>
{
    public void Configure(EntityTypeBuilder<VehicleSegment> e)
    {
        e.ToTable("Segmentler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- VehicleType / Araç tip (tenant-owned; master sözlük) ----
internal sealed class VehicleTypeConfig : IEntityTypeConfiguration<VehicleType>
{
    public void Configure(EntityTypeBuilder<VehicleType> e)
    {
        e.ToTable("AracTipleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Marka).HasMaxLength(64);
        e.Property(x => x.Vites).HasMaxLength(32);  // roadmap K1
        e.Property(x => x.Yakit).HasMaxLength(32);  // roadmap K1
        e.Property(x => x.Grup).HasMaxLength(64);   // roadmap K1
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- VehicleOwner / Araç sahip (tenant-owned; master sözlük) ----
internal sealed class VehicleOwnerConfig : IEntityTypeConfiguration<VehicleOwner>
{
    public void Configure(EntityTypeBuilder<VehicleOwner> e)
    {
        e.ToTable("AracSahipleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Tur).HasMaxLength(32);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- ExpenseCategory / Gider türü (tenant-owned; master sözlük) ----
internal sealed class ExpenseCategoryConfig : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> e)
    {
        e.ToTable("GiderTurleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- FinancialAccount / Kasa-Banka hesap (tenant-owned; master sözlük) ----
internal sealed class FinancialAccountConfig : IEntityTypeConfiguration<FinancialAccount>
{
    public void Configure(EntityTypeBuilder<FinancialAccount> e)
    {
        e.ToTable("Hesaplar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Tur).HasMaxLength(32);
        e.Property(x => x.Doviz).HasMaxLength(3);
        e.Property(x => x.Iban).HasMaxLength(34);     // roadmap K1 (IBAN max 34)
        e.Property(x => x.HesapNo).HasMaxLength(64);  // roadmap K1
        e.Property(x => x.Banka).HasMaxLength(128);   // roadmap K1
        e.Property(x => x.Sube).HasMaxLength(128);    // roadmap K1
        // FAZ-20 sözlük derinliği
        e.Property(x => x.OzelKod).HasMaxLength(32);
        e.Property(x => x.UyariMailListesi).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- CustomCode / Özel kod (tenant-owned; master sözlük) ----
internal sealed class CustomCodeConfig : IEntityTypeConfiguration<CustomCode>
{
    public void Configure(EntityTypeBuilder<CustomCode> e)
    {
        e.ToTable("OzelKodlar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- Currency / Döviz (tenant-owned; master sözlük) ----
internal sealed class CurrencyConfig : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> e)
    {
        e.ToTable("Dovizler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(3);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Sembol).HasMaxLength(8);
        e.Property(x => x.Ulke).HasMaxLength(64);   // FAZ-20
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

// ---- HesapKodu / ServisTanim (basit Kod-master; full-CRUD → roadmap N1) ----
internal sealed class HesapKoduConfig : IEntityTypeConfiguration<HesapKodu>
{
    public void Configure(EntityTypeBuilder<HesapKodu> e)
    {
        e.ToTable("HesapKodlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.Ad).IsRequired().HasMaxLength(200);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
    }
}

internal sealed class ServisTanimConfig : IEntityTypeConfiguration<ServisTanim>
{
    public void Configure(EntityTypeBuilder<ServisTanim> e)
    {
        e.ToTable("ServisTanimlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kod).IsRequired().HasMaxLength(32);
        e.Property(x => x.AracTipi).IsRequired().HasMaxLength(100);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        // FAZ-14 C: filo kombinasyonu (additive, opsiyonel).
        e.Property(x => x.Marka).HasMaxLength(100);
        e.Property(x => x.Tip).HasMaxLength(100);
        e.Property(x => x.Yakit).HasMaxLength(32);
        e.Property(x => x.Vites).HasMaxLength(32);
        e.HasIndex(x => new { x.TenantId, x.Kod }).IsUnique();
        // Kombinasyon UNIQUE DEĞİL: aynı kombinasyona farklı kod/açıklamayla birden çok tanım
        // girmek meşru (ör. km aralığı revizyonu). Öneri motoru "eşleşen var mı"ya bakar.
        e.HasIndex(x => new { x.TenantId, x.Marka, x.Tip, x.Yakit, x.Vites });
    }
}

internal sealed class DropTanimConfig : IEntityTypeConfiguration<DropTanim>
{
    public void Configure(EntityTypeBuilder<DropTanim> e)
    {
        e.ToTable("DropTanimlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Lokasyon).IsRequired().HasMaxLength(150);
        e.Property(x => x.Sube).IsRequired().HasMaxLength(150);
        e.Property(x => x.KarsilamaSekli).HasMaxLength(100);
        e.Property(x => x.CalismaSekli).HasMaxLength(100);
        e.Property(x => x.OzelIletisim).HasMaxLength(200);
        e.Property(x => x.Ucret).HasColumnType("numeric(19,4)"); // FAZ 3.A3b
        e.HasIndex(x => new { x.TenantId, x.Lokasyon, x.Sube }).IsUnique();
    }
}


// ---- SubeUcretsizHizmet (FAZ-23 — şubeye özel ücretsiz hizmet; tenant-owned, para taşımaz) ----
internal sealed class SubeUcretsizHizmetConfig : IEntityTypeConfiguration<SubeUcretsizHizmet>
{
    public void Configure(EntityTypeBuilder<SubeUcretsizHizmet> e)
    {
        e.ToTable("SubeUcretsizHizmetler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.HizmetAdi).IsRequired().HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        // Composite tenant-FK: çapraz-tenant referans imkansız. Şube silinirse satırlar da gider
        // (child kayıt; başsız kalması anlamsız).
        e.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SubeId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.TenantId, x.SubeId });
    }
}
