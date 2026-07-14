using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Periyodik faturalama dönem satırı (FAZ 4.2-B1). Uzun/aylık kirada plan: her satır bir fatura
/// dönemi — idempotency + UI durum listesi bu tablodan. Kesildi satır Invoice'a bağlanır ve ASLA
/// yeniden üretilmez; Planlandi gelecek satırlar plan değişiminde (uzatma) yeniden üretilir;
/// Atlandi = cap nedeniyle kesilemeyen dönem (erken dönüş). Benzersiz: (TenantId, RentalId, DonemSira).
/// </summary>
public class FaturaDonemi : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid RentalId { get; set; }
    /// <summary>1-tabanlı dönem sırası (fatura kesiminde deterministik anahtarın parçası).</summary>
    public int DonemSira { get; set; }
    public DateTimeOffset DonemBas { get; set; }
    public DateTimeOffset DonemBit { get; set; }

    public FaturaDonemDurum Durum { get; set; } = FaturaDonemDurum.Planlandi;
    public Guid? InvoiceId { get; set; }
    /// <summary>Kesilen brüt tutar (Kesildi'de dolu; bilgi/iz).</summary>
    public decimal? KesilenTutar { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
