using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

/// <summary>Müşteri taksiti (FAZ-66). Takip kaydı — mali belge DEĞİL, tam CRUD.</summary>
internal sealed class CustomerInstallmentConfig : IEntityTypeConfiguration<MusteriTaksit>
{
    public void Configure(EntityTypeBuilder<MusteriTaksit> e)
    {
        e.ToTable("MusteriTaksitleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();

        // Composite tenant-FK: çapraz-tenant referans yapısal olarak imkânsız.
        e.HasOne<Customer>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CariId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Vehicle>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VehicleId })
            .HasPrincipalKey(v => new { v.TenantId, v.Id })
            .OnDelete(DeleteBehavior.Restrict);

        e.Property(x => x.TaksitTutari).HasColumnType("numeric(19,4)");
        e.Property(x => x.Kur).HasColumnType("numeric(19,4)");
        e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.Aciklama).HasMaxLength(512);

        e.Ignore(x => x.TutarBaz);
        e.Ignore(x => x.Gecikti);   // TÜRETİLMİŞ — kolon değil

        e.HasIndex(x => new { x.TenantId, x.CariId });
        e.HasIndex(x => new { x.TenantId, x.Vade });
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
    }
}
