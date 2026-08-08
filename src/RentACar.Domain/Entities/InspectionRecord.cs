using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>Araç muayene kaydı. Bitiş (genelde +2 yıl) vade panosunu besler.</summary>
public class InspectionRecord : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid VehicleId { get; set; }

    public DateTimeOffset MuayeneTarihi { get; set; }
    public DateTimeOffset Bitis { get; set; }
    public decimal Ucret { get; set; }

    /// <summary>Gecikme/eksik cezası (roadmap J2): ödemede girilir, TOPLAM borcu artırır.</summary>
    public decimal Ceza { get; set; }

    /// <summary>
    /// Ödenmemiş bakiye (FAZ-14 kısmi ödeme). Oluşturulunca = <see cref="Ucret"/>; ödemede
    /// girilen ceza bunu ARTIRIR, ödenen tutar düşürür. TRY.
    /// </summary>
    public decimal Kalan { get; set; }

    /// <summary>Muayene anındaki araç km'si (canlı parite; bilgi alanı, hesaba girmez).</summary>
    public int? IslemKm { get; set; }

    /// <summary>Kayıt notu (ödeme açıklamasından ayrı).</summary>
    public string? Aciklama { get; set; }
    /// <summary>Ödendi mi (roadmap J2): true → defter kaydı yazılmış, tekrar ödenemez.</summary>
    public bool Odendi { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
