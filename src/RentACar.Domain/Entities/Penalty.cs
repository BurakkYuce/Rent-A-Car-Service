using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Trafik cezası. Tenant-owned + auditable. Tebliğ tarihinden vade hesaplanır. Müşteriye
/// yansıtılınca (Yansitildi) cari borçlanır (Borç Cari / Alacak Gelir) — yansıtma kaydı
/// immutable. Ceza başlığı güncellenebilir (durum/ödeme), ama yansıtma defteri değişmez.
/// </summary>
public class Penalty : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (CZ-000001).</summary>
    public string No { get; set; } = string.Empty;

    public string CezaTuru { get; set; } = string.Empty;
    public DateTimeOffset TebligTarihi { get; set; }
    public DateTimeOffset VadeTarihi { get; set; }

    public Guid? VehicleId { get; set; }
    public Guid? CariId { get; set; }       // yansıtılacak müşteri
    public Guid? RentalId { get; set; }      // ilgili kira sözleşmesi

    public decimal Tutar { get; set; }
    public string? Sebep { get; set; }

    public CezaDurum Durum { get; set; } = CezaDurum.Yeni;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
