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
        e.HasIndex(x => new { x.TenantId, x.ServiceRecordId });
    }
}
