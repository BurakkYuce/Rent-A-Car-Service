using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Tenant-başına boşluksuz, rollback-safe sıra üreteci. Tahsis, kaydın eklendiği
/// AYNI transaction içinde yapılır → transaction geri alınırsa numara da geri alınır
/// (boşluk olmaz). Eşzamanlı tahsisler satır kilidiyle serileşir.
/// (DB auto-increment kullanılamaz: rollback boşluk yaratır — TR mali mevzuatı.)
/// </summary>
public class TenantSequence : ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>Sıra adı: "RentalNo", "ReservationNo", (ileride) "InvoiceNo"...</summary>
    public string Name { get; set; } = string.Empty;

    public long NextValue { get; set; }
}
