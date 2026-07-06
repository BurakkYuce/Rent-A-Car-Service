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
        e.Property(x => x.Currency).HasMaxLength(3);
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
        e.HasIndex(x => new { x.TenantId, x.Bitis });
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
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.CariId });
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
    }
}
