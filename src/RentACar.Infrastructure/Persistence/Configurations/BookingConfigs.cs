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
        e.Property(x => x.KomisyonTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.DropUcreti).HasColumnType("numeric(19,4)");
        e.Property(x => x.SonraOdeOran).HasColumnType("numeric(9,4)");
        e.Property(x => x.Aciklama).HasMaxLength(1024);
        e.HasIndex(x => new { x.TenantId, x.ReservationNo }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
    }
}

// ---- Quotation / Teklif (tenant-owned; operasyonel, güncellenebilir) ----
internal sealed class QuotationConfig : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> e)
    {
        e.ToTable("Quotations");
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
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
        e.HasIndex(x => new { x.TenantId, x.Durum });
    }
}

// ---- RentalContract (tenant-owned) ----
internal sealed class RentalContractConfig : IEntityTypeConfiguration<RentalContract>
{
    public void Configure(EntityTypeBuilder<RentalContract> e)
    {
        e.ToTable("Rentals");
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
