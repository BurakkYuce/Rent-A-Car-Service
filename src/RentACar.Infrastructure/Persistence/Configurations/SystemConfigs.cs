// Sistem/altyapi tablolari: denetim logu, bosluksuz sira sayaci.
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi
// dongu tum ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- AuditLog (tenant-owned) ----
internal sealed class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> e)
    {
        e.ToTable("AuditLogs");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.EntityName).IsRequired().HasMaxLength(128);
        e.Property(x => x.EntityId).IsRequired().HasMaxLength(64);
        e.Property(x => x.Action).HasConversion<int>();
        e.Property(x => x.UserName).HasMaxLength(128);
        e.Property(x => x.OldValues).HasColumnType("jsonb");
        e.Property(x => x.NewValues).HasColumnType("jsonb");
        e.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId });
    }
}

// ---- TenantSequence (tenant-owned; boşluksuz sıra) ----
internal sealed class TenantSequenceConfig : IEntityTypeConfiguration<TenantSequence>
{
    public void Configure(EntityTypeBuilder<TenantSequence> e)
    {
        e.ToTable("TenantSequences");
        e.HasKey(x => new { x.TenantId, x.Name });
        e.Property(x => x.Name).HasMaxLength(64);
    }
}
