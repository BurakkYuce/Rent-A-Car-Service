using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Teklif (quotation). Tenant-owned + auditable. Bir müşteri + araç + tarih aralığı için
/// fiyatlı öneri. Kabul edilince rezervasyona (Reservation) dönüşür; rezervasyon de kiraya.
/// Fiyat manuel günlük ücretle hesaplanır (BookingMath; fiyat motoru ertelendi).
/// </summary>
public class Quotation : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz sıra (örn. TK-000001).</summary>
    public string No { get; set; } = string.Empty;

    public QuotationStatus Durum { get; set; } = QuotationStatus.Taslak;

    public Guid MusteriId { get; set; }
    public Guid VehicleId { get; set; }

    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }

    public string? CikisOfisi { get; set; }
    public string? DonusOfisi { get; set; }

    public int Gun { get; set; }
    public decimal GunlukUcret { get; set; }
    public decimal Tutar { get; set; }

    // Anlaşılan aşım koşulları (rezervasyona/kiraya taşınır).
    public int KmLimit { get; set; }
    public decimal FazlaKmUcret { get; set; }
    public decimal YakitBirimUcret { get; set; }

    /// <summary>Teklifin geçerlilik (son) tarihi. Bilgi amaçlı; otomatik iptal yok (v1).</summary>
    public DateTimeOffset? GecerlilikTarihi { get; set; }

    public string? Aciklama { get; set; }

    /// <summary>Kabul sonrası oluşan rezervasyon.</summary>
    public Guid? ReservationId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
