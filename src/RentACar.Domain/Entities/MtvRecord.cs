using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>Araç MTV (motorlu taşıtlar vergisi) dönem kaydı. Vade panosunu besler.</summary>
public class MtvRecord : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid VehicleId { get; set; }

    /// <summary>Dönem, ör. "2026-1" / "2026-2".</summary>
    public string Donem { get; set; } = string.Empty;
    public decimal Tutar { get; set; }
    public DateTimeOffset Vade { get; set; }

    /// <summary>
    /// Ödenmemiş bakiye (FAZ-14 kısmi ödeme). Oluşturulunca = <see cref="Tutar"/>; her
    /// <see cref="MtvOdeme"/> bunu düşürür, 0'a inince <see cref="Odendi"/> true olur.
    /// Tutar ile birlikte TRY'dir (MTV bir devlet vergisi — kayıtta döviz alanı YOKTUR).
    /// </summary>
    public decimal Kalan { get; set; }

    /// <summary>Ödendi mi — türetilmiş bayrak: Kalan &lt;= 0 olduğunda true'ya çekilir.</summary>
    public bool Odendi { get; set; }

    /// <summary>Kayıt notu (ödeme açıklamasından AYRI — o ödemenin kendi alanı).</summary>
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
