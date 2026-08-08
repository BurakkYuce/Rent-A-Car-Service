using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// FAZ-45 — personel VARDİYA dikeyi. Personel master'ın kendi config'i tarihsel olarak
// CustomerConfigs.cs'te (CRM kümesi); yeni dikey oraya eklenmedi, ayrı dosya açıldı.

/// <summary>Personel vardiyası (FAZ-45). Aynı personelin aynı gün birden çok vardiyası olabilir
/// (bölünmüş mesai) → indeks UNIQUE DEĞİL; çakışma kontrolü servis katmanında (gece vardiyası
/// gün sınırını aştığı için tek-tablo unique kısıtıyla ifade edilemez).</summary>
internal sealed class PersonelVardiyaConfig : IEntityTypeConfiguration<PersonelVardiya>
{
    public void Configure(EntityTypeBuilder<PersonelVardiya> e)
    {
        e.ToTable("PersonelVardiyalari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();

        // Composite tenant-FK: çapraz-tenant referans yapısal olarak imkansız.
        e.HasOne<Personel>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PersonelId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SubeId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .OnDelete(DeleteBehavior.Restrict);

        e.Property(x => x.Tarih).HasColumnType("date");
        e.Property(x => x.BaslangicSaat).HasColumnType("time");
        e.Property(x => x.BitisSaat).HasColumnType("time");
        e.Property(x => x.Sube).HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);

        e.HasIndex(x => new { x.TenantId, x.PersonelId, x.Tarih });
        e.HasIndex(x => new { x.TenantId, x.Tarih });     // matris sorgusu tarih aralığıyla süzer
        e.HasIndex(x => x.SubeId);

        e.Ignore(x => x.SureDk);                          // türetilmiş (kolon değil)
    }
}
