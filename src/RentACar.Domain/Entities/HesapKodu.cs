using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Muhasebe hesap-kodu sözlüğü (roadmap N1): kullanıcı tanımlı muhasebe hesap kodları (Kod/Ad/Açıklama).
/// Tenant-owned + auditable; full-CRUD basit master. FinancialAccount (kasa/banka) ile FARKLI kavram.
/// </summary>
public class HesapKodu : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
