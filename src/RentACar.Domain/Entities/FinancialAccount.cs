using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Kasa/Banka hesap tanımı (master sözlük): nakit kasaları ve banka hesaplarının adlandırılmış
/// listesi (ör. "Merkez Kasa", "Ziraat TL", "İş Bankası USD"). Tenant-owned + auditable. Tahsilat/
/// ödeme/virman formlarındaki Hesap açılır listesini besler (additive — defter AccountRef serbest).
/// </summary>
public class FinancialAccount : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    /// <summary>Hesap türü (serbest metin, ör. "Kasa", "Banka"). Opsiyonel.</summary>
    public string? Tur { get; set; }
    /// <summary>Hesap para birimi (3 harf, ör. "TRY", "USD"). Opsiyonel.</summary>
    public string? Doviz { get; set; }
    /// <summary>IBAN (banka hesapları için) — roadmap K1.</summary>
    public string? Iban { get; set; }
    /// <summary>Banka hesap numarası — roadmap K1.</summary>
    public string? HesapNo { get; set; }
    /// <summary>Banka adı — roadmap K1.</summary>
    public string? Banka { get; set; }
    /// <summary>Banka şubesi — roadmap K1.</summary>
    public string? Sube { get; set; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
