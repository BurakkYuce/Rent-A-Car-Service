using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Döviz tanımı (master sözlük): tenant'ın kullandığı para birimleri (TRY, USD, EUR…).
/// Tenant-owned + auditable. Kasa/banka tahsilat-ödeme ve fatura döviz açılır listelerini besler.
/// Kod 3 harfli ISO kodu (büyük harf). Kur burada SAKLANMAZ (anlık kur işlem anında girilir).
/// </summary>
public class Currency : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>ISO 3 harf kodu (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Sembol { get; set; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
