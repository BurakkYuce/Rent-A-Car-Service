using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Kira sözleşmesi (sistemin kalbi — PR #3 çekirdeği). Tenant-owned + auditable.
/// Aktif (Kirada) sözleşmeler için araç+tarih çakışması DB-seviyesi exclusion
/// constraint ile engellenir (double-booking koruması). Teslim (Çıkış KM/yakıt) ve
/// dönüş (Dönüş KM/yakıt/uzatma) alanları PR #4'te doldurulur.
/// </summary>
public class RentalContract : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz sözleşme no (örn. KS-000001).</summary>
    public string SozlesmeNo { get; set; } = string.Empty;

    public RentalStatus Durum { get; set; } = RentalStatus.Kirada;

    /// <summary>Hangi rezervasyondan dönüştü (varsa).</summary>
    public Guid? ReservationId { get; set; }

    public Guid MusteriId { get; set; }
    public Guid VehicleId { get; set; }

    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }

    public string? CikisOfisi { get; set; }
    public string? DonusOfisi { get; set; }

    // Teslim (PR #4)
    public int? CikisKm { get; set; }
    public int? CikisYakit { get; set; }

    // Dönüş (PR #4)
    public int? DonusKm { get; set; }
    public int? DonusYakit { get; set; }
    public DateTimeOffset? GercekDonusTar { get; set; }

    public int Gun { get; set; }
    public decimal GunlukUcret { get; set; }
    public decimal Tutar { get; set; }
    public decimal Tahsilat { get; set; }
    public decimal Bakiye { get; set; }

    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
