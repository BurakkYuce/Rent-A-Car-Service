using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Banka tanımı (master sözlük): bankaların adlandırılmış listesi (ör. "Ziraat", "İş Bankası",
/// "Garanti"). Tenant-owned + auditable. Banka hesabı/çek-senet formlarındaki Banka açılır listesini
/// besler (additive — serbest metin alan string kalır).
/// </summary>
public class Bank : ITenantOwned, IAuditable
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
