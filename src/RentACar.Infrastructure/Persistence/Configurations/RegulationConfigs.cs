// Regulasyon tablolari: sigorta police, MTV, muayene, ceza.
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi
// dongu tum ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- Regülasyon (tenant-owned; güncellenebilir, mali belge değil) ----
internal sealed class InsurancePolicyConfig : IEntityTypeConfiguration<InsurancePolicy>
{
    public void Configure(EntityTypeBuilder<InsurancePolicy> e)
    {
        e.ToTable("InsurancePolicies");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Tip).HasConversion<int>();
        e.Property(x => x.PoliceNo).HasMaxLength(64);
        e.Property(x => x.Firma).HasMaxLength(128);
        e.Property(x => x.Acenta).HasMaxLength(128);
        e.Property(x => x.Prim).HasColumnType("numeric(19,4)");
        e.Property(x => x.ZeyilPrim).HasColumnType("numeric(19,4)"); // roadmap J3
        // FAZ-15 bilgi alanları (deftere girmez) + poliçe bakiyesi.
        e.Property(x => x.AracDegeri).HasColumnType("numeric(19,4)");
        e.Property(x => x.ImmDegeri).HasColumnType("numeric(19,4)");
        e.Property(x => x.AksesuarDegeri).HasColumnType("numeric(19,4)");
        e.Property(x => x.Kalan).HasColumnType("numeric(19,4)");
        e.Property(x => x.Currency).HasMaxLength(3);
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
        e.HasIndex(x => new { x.TenantId, x.Bitis });
        // Zeyil composite tenant-FK'sinin hedefi (TenantId, Id) — tenant sınırını FK'nin
        // KENDİSİ taşır; başka tenant'ın poliçesine zeyil bağlanamaz.
        e.HasAlternateKey(x => new { x.TenantId, x.Id });
    }
}

/// <summary>
/// FAZ-15 — poliçe zeyli (poliçe eki). Mali belge DEĞİL: deftere hiç yazmaz, yalnız bilgi/geçmiş
/// tutar → değişmezlik trigger'ı YOK, tam CRUD (yanlış girilen zeyil silinebilmeli).
/// </summary>
internal sealed class InsurancePolicyEndorsementConfig : IEntityTypeConfiguration<InsurancePolicyZeyil>
{
    public void Configure(EntityTypeBuilder<InsurancePolicyZeyil> e)
    {
        e.ToTable("InsurancePolicyZeyilleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.HasOne<InsurancePolicy>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PolicyId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);   // poliçe silinirse ekleri de gider (bilgi kaydı)
        e.Property(x => x.ZeyilNo).IsRequired().HasMaxLength(32);
        e.Property(x => x.Deger).HasColumnType("numeric(19,4)");
        e.Property(x => x.Brut).HasColumnType("numeric(19,4)");
        e.Property(x => x.Net).HasColumnType("numeric(19,4)");
        e.Property(x => x.FonVergi).HasColumnType("numeric(19,4)");
        e.Property(x => x.Tipi).HasMaxLength(64);
        e.Property(x => x.Neden).HasMaxLength(512);
        // Doğal anahtar: aynı poliçeye aynı zeyil no iki kez girilemez (mükerrer geçmiş satırı).
        e.HasIndex(x => new { x.TenantId, x.PolicyId, x.ZeyilNo }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.Tarih });
    }
}

internal sealed class MtvRecordConfig : IEntityTypeConfiguration<MtvRecord>
{
    public void Configure(EntityTypeBuilder<MtvRecord> e)
    {
        e.ToTable("MtvRecords");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Donem).IsRequired().HasMaxLength(16);
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.Kalan).HasColumnType("numeric(19,4)");   // FAZ-14 kısmi ödeme bakiyesi
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
        e.HasIndex(x => new { x.TenantId, x.Vade });
    }
}

internal sealed class InspectionRecordConfig : IEntityTypeConfiguration<InspectionRecord>
{
    public void Configure(EntityTypeBuilder<InspectionRecord> e)
    {
        e.ToTable("InspectionRecords");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Ucret).HasColumnType("numeric(19,4)");
        e.Property(x => x.Ceza).HasColumnType("numeric(19,4)"); // roadmap J2
        e.Property(x => x.Kalan).HasColumnType("numeric(19,4)");   // FAZ-14 kısmi ödeme bakiyesi
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
        e.HasIndex(x => new { x.TenantId, x.Bitis });
    }
}

// ---- Penalty / Ceza (tenant-owned; başlık güncellenebilir, yansıtma defteri immutable) ----
internal sealed class PenaltyConfig : IEntityTypeConfiguration<Penalty>
{
    public void Configure(EntityTypeBuilder<Penalty> e)
    {
        e.ToTable("Penalties");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.CezaTuru).IsRequired().HasMaxLength(128);
        e.Property(x => x.Sebep).HasMaxLength(512);
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.Durum).HasConversion<int>();
        // FAZ-60 kısmi ödeme toplamları (satırlardan türetilir, önbellek).
        e.Property(x => x.OdenenTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.Kalan).HasColumnType("numeric(19,4)");
        // FAZ-60 bilgi alanları (canlı parite; deftere girmez).
        e.Property(x => x.Saat).HasMaxLength(8);
        e.Property(x => x.Yer).HasMaxLength(256);
        e.Property(x => x.CepTel).HasMaxLength(32);
        e.Property(x => x.MakbuzNo).HasMaxLength(64);
        e.Property(x => x.IslemSube).HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.CariId });
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
        e.HasIndex(x => new { x.TenantId, x.MakbuzNo });   // FAZ-60 filtre
        // Alt tabloların composite FK'si için alternatif anahtar (tenant sızıntısına karşı:
        // çocuk satır BAŞKA tenant'ın cezasına bağlanamaz).
        e.HasAlternateKey(x => new { x.TenantId, x.Id });
    }
}

// ---- FAZ-60 ceza kalemi + kalem ödemesi ----

internal sealed class PenaltyLineConfig : IEntityTypeConfiguration<PenaltySatir>
{
    public void Configure(EntityTypeBuilder<PenaltySatir> e)
    {
        e.ToTable("PenaltySatirlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.HasOne<Penalty>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PenaltyId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.Odenen).HasColumnType("numeric(19,4)");
        e.Property(x => x.Kalan).HasColumnType("numeric(19,4)");
        e.Property(x => x.Sebep).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.PenaltyId, x.Sira }).IsUnique();
        // Ödeme satırının composite FK'si için.
        e.HasAlternateKey(x => new { x.TenantId, x.Id });
    }
}

internal sealed class PenaltyPaymentConfig : IEntityTypeConfiguration<PenaltyOdeme>
{
    public void Configure(EntityTypeBuilder<PenaltyOdeme> e)
    {
        e.ToTable("PenaltyOdemeleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.HasOne<PenaltySatir>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SatirId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);   // ödemesi olan satır silinemez
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.KalanSonrasi).HasColumnType("numeric(19,4)");
        e.Property(x => x.Hesap).HasConversion<int>();
        e.Property(x => x.Anahtar).IsRequired().HasMaxLength(160);
        e.Property(x => x.KasaKodu).HasMaxLength(64);
        e.Property(x => x.HesapNo).HasMaxLength(64);
        e.Property(x => x.MakbuzNo).HasMaxLength(64);
        e.Property(x => x.IslemYapan).HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        // DETERMİNİSTİK idempotency: "ceza:{cezaId}:satir:{satirId}:odeme:{sira}" — MONOTON
        // bileşen sira. Aynı sıra ikinci kez yazılamaz (yarış kaçarsa DB tutar).
        e.HasIndex(x => new { x.TenantId, x.Anahtar }).IsUnique();
        // Form çift-submit koruması (kısmi index — Expense deseni).
        e.HasIndex(x => new { x.TenantId, x.IslemAnahtari })
            .IsUnique()
            .HasFilter("\"IslemAnahtari\" IS NOT NULL");
        e.HasIndex(x => new { x.TenantId, x.PenaltyId });
    }
}

// ---- FAZ-14 kısmi ödeme çocukları (MALİ BELGE: app'e yalnız SELECT/INSERT verilir) ----

internal sealed class MtvPaymentConfig : IEntityTypeConfiguration<MtvOdeme>
{
    public void Configure(EntityTypeBuilder<MtvOdeme> e)
    {
        e.ToTable("MtvOdemeleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.HasOne<MtvRecord>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.MtvId })
            .HasPrincipalKey(m => new { m.TenantId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);   // ödemesi olan kayıt silinemez
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.KalanSonrasi).HasColumnType("numeric(19,4)");
        e.Property(x => x.Hesap).HasConversion<int>();
        e.Property(x => x.KasaKodu).HasMaxLength(64);
        e.Property(x => x.HesapNo).HasMaxLength(64);
        e.Property(x => x.EvrakNo).HasMaxLength(64);
        e.Property(x => x.IslemYapan).HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        // Sıra benzersizliği: satır kilidi yarışı kaçarsa DB tutar (aynı sıra iki kez yazılamaz).
        e.HasIndex(x => new { x.TenantId, x.MtvId, x.Sira }).IsUnique();
        // Çift-submit: aynı işlem anahtarıyla ikinci ödeme yazılamaz (Expense deseni).
        e.HasIndex(x => new { x.TenantId, x.IslemAnahtari })
            .IsUnique()
            .HasFilter("\"IslemAnahtari\" IS NOT NULL");
    }
}

internal sealed class InspectionPaymentConfig : IEntityTypeConfiguration<MuayeneOdeme>
{
    public void Configure(EntityTypeBuilder<MuayeneOdeme> e)
    {
        e.ToTable("MuayeneOdemeleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.HasOne<InspectionRecord>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.InspectionId })
            .HasPrincipalKey(m => new { m.TenantId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.Ceza).HasColumnType("numeric(19,4)");
        e.Property(x => x.KalanSonrasi).HasColumnType("numeric(19,4)");
        e.Property(x => x.Hesap).HasConversion<int>();
        e.Property(x => x.KasaKodu).HasMaxLength(64);
        e.Property(x => x.HesapNo).HasMaxLength(64);
        e.Property(x => x.EvrakNo).HasMaxLength(64);
        e.Property(x => x.IslemYapan).HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.InspectionId, x.Sira }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.IslemAnahtari })
            .IsUnique()
            .HasFilter("\"IslemAnahtari\" IS NOT NULL");
    }
}
