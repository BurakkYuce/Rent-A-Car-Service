using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Rezervasyon. Tenant-owned + auditable. Bir araç + tarih aralığı için ön kayıt;
/// Tasfiye ile kira sözleşmesine dönüşür. (PR #3: fiyat manuel günlük ücret; fiyat
/// motoru ertelendi.)
/// </summary>
public class Reservation : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz sıra (örn. RZ-000001).</summary>
    public string ReservationNo { get; set; } = string.Empty;

    public ReservationStatus Durum { get; set; } = ReservationStatus.Rezerv;

    public Guid MusteriId { get; set; }
    public Guid VehicleId { get; set; }

    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }

    public string? CikisOfisi { get; set; }
    public string? DonusOfisi { get; set; }

    public int Gun { get; set; }
    public decimal GunlukUcret { get; set; }
    public decimal Tutar { get; set; }

    // Anlaşılan aşım koşulları (kiraya çevrilirken sözleşmeye taşınır).
    public int KmLimit { get; set; }
    public decimal FazlaKmUcret { get; set; }
    public decimal YakitBirimUcret { get; set; }

    public string? Aciklama { get; set; }

    /// <summary>Tasfiye sonrası oluşan kira sözleşmesi.</summary>
    public Guid? RentalContractId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
