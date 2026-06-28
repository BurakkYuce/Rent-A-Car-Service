using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Ödeme tipi tanımı (master sözlük): tahsilat/ödeme yöntemlerinin adlandırılmış listesi (ör. "Nakit",
/// "Kredi Kartı", "Havale/EFT", "Çek"). Tenant-owned + auditable. Kasa/banka formlarındaki Ödeme Tipi
/// açılır listesini besler (additive). NOT: CashTransactionType enum'undan (Tahsilat/Ödeme yönü) farklı.
/// </summary>
public class PaymentType : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
