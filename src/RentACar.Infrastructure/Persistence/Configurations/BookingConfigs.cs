// Rezervasyon/teklif/kira sozlesmesi + ek hizmet kalemleri.
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi
// dongu tum ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- Reservation (tenant-owned) ----
internal sealed class ReservationConfig : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> e)
    {
        e.ToTable("Reservations");
        // FAZ 5-C4: türetilmiş çıkış-şube FK'sı (composite tenant-FK; PlatformConfigs User deseni).
        e.HasIndex(x => new { x.TenantId, x.CikisSubeId });
        e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.CikisSubeId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.ReservationNo).IsRequired().HasMaxLength(32);
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.CikisOfisi).HasMaxLength(64);
        e.Property(x => x.DonusOfisi).HasMaxLength(64);
        e.Property(x => x.GunlukUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.IskontoTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.HaftaSonuFark).HasColumnType("numeric(19,4)");
        e.Property(x => x.FazlaKmUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.YakitBirimUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.Provizyon).HasColumnType("numeric(19,4)");
        e.Property(x => x.Depozito).HasColumnType("numeric(19,4)");
        e.Property(x => x.KomisyonOran).HasColumnType("numeric(9,4)");
        // FAZ 4.5 — OTA bileşen fiyatları (bilgi)
        e.Property(x => x.OtaKiraBedeli).HasColumnType("numeric(19,4)");
        e.Property(x => x.OtaDropBedeli).HasColumnType("numeric(19,4)");
        e.Property(x => x.OtaBebekKoltugu).HasColumnType("numeric(19,4)");
        e.Property(x => x.OtaNavigasyon).HasColumnType("numeric(19,4)");
        e.Property(x => x.OtaLcf).HasColumnType("numeric(19,4)");
        e.Property(x => x.OtaCdw).HasColumnType("numeric(19,4)");
        e.Property(x => x.OtaScdw).HasColumnType("numeric(19,4)");
        e.Property(x => x.OtaEkSurucu).HasColumnType("numeric(19,4)");
        e.Property(x => x.KomisyonTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.DropUcreti).HasColumnType("numeric(19,4)");
        e.Property(x => x.SonraOdeOran).HasColumnType("numeric(9,4)");
        e.Property(x => x.Aciklama).HasMaxLength(1024);
        e.Property(x => x.KampanyaKodu).HasMaxLength(64); // FAZ 3.A5
        e.Property(x => x.FiyatTuru).HasMaxLength(64); // FAZ 3.A6-B2
        e.Property(x => x.KdvOranSnapshot).HasColumnType("numeric(9,4)");
        // FAZ-48 — talep/organizasyon bilgi alanları (uzunluklar RentalContract'takiyle BİREBİR;
        // "Kiraya Çevir" kopyalaması kesilmesin).
        e.Property(x => x.TalepTuru).HasMaxLength(64);
        e.Property(x => x.GeldigiBirim).HasMaxLength(64);
        e.Property(x => x.OnayKodu).HasMaxLength(64);
        e.Property(x => x.ProjeAdi).HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.ReservationNo }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
        // FAZ-48 — liste filtresi (durum + başlangıç tarihi aralığı) için kapsayıcı indeks.
        e.HasIndex(x => new { x.TenantId, x.Durum, x.BasTar });
    }
}

// ---- Quotation / Teklif (tenant-owned; operasyonel, güncellenebilir) ----
internal sealed class QuotationConfig : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> e)
    {
        e.ToTable("Quotations");
        // FAZ 5-C4: türetilmiş çıkış-şube FK'sı (composite tenant-FK; PlatformConfigs User deseni).
        e.HasIndex(x => new { x.TenantId, x.CikisSubeId });
        e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.CikisSubeId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.CikisOfisi).HasMaxLength(64);
        e.Property(x => x.DonusOfisi).HasMaxLength(64);
        e.Property(x => x.GunlukUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.IskontoTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.HaftaSonuFark).HasColumnType("numeric(19,4)");
        e.Property(x => x.FazlaKmUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.YakitBirimUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.Aciklama).HasMaxLength(1024);
        e.Property(x => x.FiyatTuru).HasMaxLength(64); // FAZ 3.A6-B2
        e.Property(x => x.KdvOranSnapshot).HasColumnType("numeric(9,4)");
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
        e.HasIndex(x => new { x.TenantId, x.Durum });
    }
}

// ---- FaturaDonemi (FAZ 4.2-B1 — periyodik fatura dönem planı; Kesildi satır Invoice'a bağlı) ----
internal sealed class FaturaDonemiConfig : IEntityTypeConfiguration<FaturaDonemi>
{
    public void Configure(EntityTypeBuilder<FaturaDonemi> e)
    {
        e.ToTable("FaturaDonemleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.KesilenTutar).HasColumnType("numeric(19,4)");
        e.HasIndex(x => new { x.TenantId, x.RentalId, x.DonemSira }).IsUnique();
    }
}

// ---- RentalContract (tenant-owned) ----
internal sealed class RentalContractConfig : IEntityTypeConfiguration<RentalContract>
{
    public void Configure(EntityTypeBuilder<RentalContract> e)
    {
        e.ToTable("Rentals");
        // FAZ 5-C4: türetilmiş çıkış-şube FK'sı (composite tenant-FK; PlatformConfigs User deseni).
        e.HasIndex(x => new { x.TenantId, x.CikisSubeId });
        e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.CikisSubeId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.SozlesmeNo).IsRequired().HasMaxLength(32);
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.CikisOfisi).HasMaxLength(64);
        e.Property(x => x.DonusOfisi).HasMaxLength(64);
        e.Property(x => x.BitisSebebi).HasMaxLength(64); // dönüş sebebi (PR2 additive)
        e.Property(x => x.IskontoTutar).HasColumnType("numeric(19,4)");  // tam teklif bileşenleri (PR4b)
        e.Property(x => x.HaftaSonuFark).HasColumnType("numeric(19,4)");
        e.Property(x => x.GunlukUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.GenelToplam).HasColumnType("numeric(19,4)");
        e.Property(x => x.Tahsilat).HasColumnType("numeric(19,4)");
        e.Property(x => x.Bakiye).HasColumnType("numeric(19,4)");
        e.Property(x => x.KurSnapshot).HasColumnType("numeric(19,6)").HasDefaultValue(1m); // O5 raporlama kuru

        e.Property(x => x.OzelKdvOran).HasColumnType("numeric(9,4)");   // FAZ 1.4
        e.Property(x => x.DamgaVergisi).HasColumnType("numeric(19,4)"); // FAZ 1.4
        e.Property(x => x.FazlaKmUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.FazlaKmBedeli).HasColumnType("numeric(19,4)");
        e.Property(x => x.YakitBirimUcret).HasColumnType("numeric(19,4)");
        e.Property(x => x.YakitBedeli).HasColumnType("numeric(19,4)");
        e.Property(x => x.UzatmaBedeli).HasColumnType("numeric(19,4)");
        e.Property(x => x.Provizyon).HasColumnType("numeric(19,4)");
        e.Property(x => x.Depozito).HasColumnType("numeric(19,4)");
        e.Property(x => x.KomisyonOran).HasColumnType("numeric(9,4)");
        e.Property(x => x.KomisyonTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.DropUcreti).HasColumnType("numeric(19,4)");
        e.Property(x => x.SonraOdeOran).HasColumnType("numeric(9,4)");
        e.Property(x => x.Aciklama).HasMaxLength(1024);
        // Kira formu detay alanları (mega-form; bilgi amaçlı — additive)
        e.Property(x => x.Kaynak).HasMaxLength(64);
        e.Property(x => x.KampanyaKodu).HasMaxLength(64); // FAZ 3.A5
        e.Property(x => x.KdvOranSnapshot).HasColumnType("numeric(9,4)"); // FAZ 3.A6
        e.Property(x => x.ProvizyonDurum).HasConversion<int>(); // FAZ 4.1
        e.Property(x => x.ProvizyonKapamaTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.UyariAciklama).HasMaxLength(512);
        e.Property(x => x.OzelFaturaAciklama).HasMaxLength(512);
        e.Property(x => x.UcusNo).HasMaxLength(32);
        e.Property(x => x.ProvizyonNo).HasMaxLength(64);
        e.Property(x => x.OnayKodu).HasMaxLength(64);
        e.Property(x => x.FirmaKodu).HasMaxLength(64);
        e.Property(x => x.ProjeAdi).HasMaxLength(128);
        e.Property(x => x.OzelKod).HasMaxLength(64);
        e.Property(x => x.TalepTuru).HasMaxLength(64);
        e.Property(x => x.GeldigiBirim).HasMaxLength(64);
        e.Property(x => x.KefilBilgisi).HasMaxLength(512);
        e.Property(x => x.AssistFirma).HasMaxLength(128);
        e.Property(x => x.OzelSoforBilgisi).HasMaxLength(512);
        e.Property(x => x.EkKosullar).HasMaxLength(2048);
        e.Property(x => x.OpsiyonNet).HasColumnType("numeric(19,4)"); // FAZ 4.4
        e.Property(x => x.AksLastikCikis).HasMaxLength(64);
        e.Property(x => x.AksLastikDonus).HasMaxLength(64);
        e.HasIndex(x => new { x.TenantId, x.SozlesmeNo }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
        e.HasMany(x => x.EkHizmetler).WithOne().HasForeignKey(a => a.RentalId).OnDelete(DeleteBehavior.Cascade);
        // Double-booking exclusion constraint + generated Period kolonu migration'da (raw SQL).
    }
}

// ---- RentalAddOn / Kira ek hizmet kalemi (tenant-owned; mutable, fatura öncesi) ----
internal sealed class RentalAddOnConfig : IEntityTypeConfiguration<RentalAddOn>
{
    public void Configure(EntityTypeBuilder<RentalAddOn> e)
    {
        e.ToTable("RentalAddOns");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Ad).IsRequired().HasMaxLength(128);
        e.Property(x => x.Miktar).HasColumnType("numeric(19,4)");
        e.Property(x => x.BirimNetFiyat).HasColumnType("numeric(19,4)");
        e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
        e.Property(x => x.NetTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.KdvTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.Toplam).HasColumnType("numeric(19,4)");
        e.HasIndex(x => new { x.TenantId, x.RentalId });
    }
}

// ---- PR-C: paylasilan sozlesmenin anlik goruntusu (tenant-owned + RLS; kisisel veri PDF'in icinde) ----
internal sealed class SozlesmePdfConfig : IEntityTypeConfiguration<SozlesmePdf>
{
    public void Configure(EntityTypeBuilder<SozlesmePdf> e)
    {
        e.ToTable("SozlesmePdfler");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        // Kira basina TEK anlik goruntu: "yeni surum" ayni satirin baytini degistirir.
        e.HasIndex(x => new { x.TenantId, x.RentalId }).IsUnique();
    }
}
