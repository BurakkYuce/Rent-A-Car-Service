// HGS geçişleri (dosyadan içe aktarım). NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki
// merkezi döngü tüm ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

internal sealed class HgsCrossingConfig : IEntityTypeConfiguration<HgsCrossing>
{
    /// <summary>Doğal anahtar index'i — adı bilerek <c>_Anahtar</c>/<c>_Idem</c> ile BİTMEZ: aynı geçişin ikinci kez
    /// gelmesi beklenen bir durumdur (depo kilit altında eler, "mükerrer" sayar), mükerrer GÖNDERİM (409) değildir.</summary>
    public const string NaturalKeyIndex = "IX_HgsGecisleri_DogalAnahtar";

    public void Configure(EntityTypeBuilder<HgsCrossing> e)
    {
        e.ToTable("HgsGecisleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Plaka).IsRequired().HasMaxLength(16);
        e.Property(x => x.EtiketNo).HasMaxLength(64);
        e.Property(x => x.AracSinifi).HasMaxLength(32);
        e.Property(x => x.GirisIstasyonu).HasMaxLength(128);
        e.Property(x => x.CikisIstasyonu).HasMaxLength(128);
        e.Property(x => x.IstasyonAnahtari).IsRequired().HasMaxLength(300);
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.HizmetOrani).HasColumnType("numeric(9,4)");
        e.Property(x => x.YansitilanTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.Kaynak).IsRequired().HasMaxLength(16);
        e.Property(x => x.DosyaAdi).HasMaxLength(256);
        e.HasIndex(x => new { x.TenantId, x.Plaka, x.GecisZamani, x.IstasyonAnahtari, x.Tutar })
            .IsUnique()
            .HasDatabaseName(NaturalKeyIndex);
        e.HasIndex(x => new { x.TenantId, x.RentalId });
        e.HasIndex(x => new { x.TenantId, x.Durum, x.GecisZamani });
        e.HasIndex(x => new { x.TenantId, x.IceAktarimId });
    }
}
