using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Şube (branch) master kaydı. Tenant-owned + auditable. Bu sürümde additive bir master
/// listedir: mevcut serbest-metin "Sube" alanlarını (Vehicle/Expense/User.AtanmisSube)
/// FK'ye çevirmez — yönetilen bir şube sözlüğü sağlar (geçerli şube adları + iletişim).
/// Form açılır listeleri buradan beslenir. (FK migrasyonu ayrı bir karar/PR.)
/// </summary>
public class Branch : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (ör. MERKEZ, ESB). Tenant içinde benzersiz, zorunlu doğal anahtar.</summary>
    public string Kod { get; set; } = string.Empty;

    /// <summary>Şube adı (ör. Merkez Ofis). Zorunlu.</summary>
    public string Ad { get; set; } = string.Empty;

    public string? Adres { get; set; }

    public string? Telefon { get; set; }

    /// <summary>Pasif şubeler açılır listelerde gizlenir ama kayıtlar korunur (silme yerine).</summary>
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
