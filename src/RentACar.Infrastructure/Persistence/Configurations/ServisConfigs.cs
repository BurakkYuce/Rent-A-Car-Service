// Servis/bakim kayitlari (+satirlar).
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi
// dongu tum ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- ServiceRecord / Servis-Bakım (tenant-owned; operasyonel, güncellenebilir) ----
internal sealed class ServiceRecordConfig : IEntityTypeConfiguration<ServiceRecord>
{
    public void Configure(EntityTypeBuilder<ServiceRecord> e)
    {
        e.ToTable("ServiceRecords");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.Tip).HasConversion<int>();
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.HasarSorumlu).HasConversion<int>();
        e.Property(x => x.AtolyeAdi).HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(1024);
        e.Property(x => x.KusurOrani).HasColumnType("numeric(5,4)");
        e.Property(x => x.ToplamIscilik).HasColumnType("numeric(19,4)");
        e.Property(x => x.YansitilanTutar).HasColumnType("numeric(19,4)"); // roadmap J4

        // FAZ-16 — kaza/hasar bloğu (BİLGİ)
        e.Property(x => x.BeyanTuru).HasMaxLength(64);
        e.Property(x => x.KarsiPlaka).HasMaxLength(32);
        e.Property(x => x.KarsiTrafikSigortasi).HasMaxLength(128);
        e.Property(x => x.KazaSorumlusu).HasMaxLength(128);
        e.Property(x => x.HasarDosyaNo).HasMaxLength(64);
        e.Property(x => x.DegerKaybi).HasColumnType("numeric(19,4)");
        // FAZ-16 — fatura bloğu (BİLGİ; deftere yazmaz — KARARLAR.md FAZ-16)
        e.Property(x => x.FaturaNo).HasMaxLength(64);
        e.Property(x => x.FaturaTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.FaturaKdv).HasColumnType("numeric(19,4)");
        e.Property(x => x.FaturaGenelToplam).HasColumnType("numeric(19,4)");
        // FAZ-16 — ödeme bloğu (BİLGİ; kasa/banka bakiyesini değiştirmez)
        e.Property(x => x.Odeme).HasColumnType("numeric(19,4)");
        e.Property(x => x.OdemeDoviz).HasMaxLength(3);
        e.Property(x => x.OdemeKur).HasColumnType("numeric(18,6)");
        e.Property(x => x.OdemeTuru).HasConversion<int>();
        e.Property(x => x.KasaKodu).HasMaxLength(32);
        e.Property(x => x.HesapNo).HasMaxLength(64);

        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
        e.HasIndex(x => new { x.TenantId, x.Durum });
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ServiceRecordId);
    }
}

internal sealed class ServiceLineConfig : IEntityTypeConfiguration<ServiceLine>
{
    public void Configure(EntityTypeBuilder<ServiceLine> e)
    {
        e.ToTable("ServiceLines");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Aciklama).IsRequired().HasMaxLength(512);
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        // FAZ-16 — canlı fiyatlandırma ızgarası bileşenleri (opsiyonel, BİLGİ).
        e.Property(x => x.BirimFiyat).HasColumnType("numeric(19,4)");
        e.Property(x => x.Miktar).HasColumnType("numeric(19,4)");
        e.Property(x => x.Indirim).HasColumnType("numeric(19,4)");
        e.Property(x => x.KdvOran).HasColumnType("numeric(5,4)"); // oran (0..1) — TUTAR değil
        e.HasIndex(x => new { x.TenantId, x.ServiceRecordId });
    }
}
