using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Hasar dosyası (BAF). Tenant-owned + auditable. Araca (ve opsiyonel kira/sorumlu cari'ye)
/// bağlı, onay akışlı kayıt — MALİ BELGE DEĞİL (güncellenebilir, defter yazmaz). Tahmini
/// tutar yalnız bilgilendirme; gerçek maliyet Gider/servis dilimlerinde işlenir.
/// </summary>
public class DamageFile : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (BAF-000001).</summary>
    public string No { get; set; } = string.Empty;

    public Guid VehicleId { get; set; }
    public Guid? RentalId { get; set; }   // hasarın oluştuğu kira
    public Guid? CariId { get; set; }      // sorumlu/karşı taraf

    public DateTimeOffset AcilisTarihi { get; set; } = DateTimeOffset.UtcNow;
    public string? Aciklama { get; set; }
    public decimal? TahminiTutar { get; set; }

    public HasarDurum Durum { get; set; } = HasarDurum.Acik;
    public string? OnayNotu { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
