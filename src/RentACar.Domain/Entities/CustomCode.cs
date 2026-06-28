using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Özel kod tanımı (master sözlük): kullanıcı tanımlı serbest sınıflandırma kodları (ör. araç/cari/
/// rezervasyon için "VIP", "Filo-A", "Kampanya-X"). Tenant-owned + auditable. Çeşitli formlardaki
/// Özel Kod açılır listesini besler (additive — ilgili serbest metin alanlar string kalır).
/// </summary>
public class CustomCode : ITenantOwned, IAuditable
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
