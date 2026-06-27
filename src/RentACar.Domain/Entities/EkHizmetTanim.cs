using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Ek hizmet tanımı (master): kiraya eklenebilen fiyatlı opsiyon (bebek koltuğu, GPS, ek sürücü,
/// kasko muafiyeti…). Tenant-owned + auditable. Kira ek hizmet kalemleri (RentalAddOn) varsayılan
/// birim ücret + KDV oranını buradan alır; serbest fiyat da girilebilir.
/// </summary>
public class EkHizmetTanim : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;

    /// <summary>Varsayılan birim NET ücret.</summary>
    public decimal BirimUcret { get; set; }

    /// <summary>KDV oranı (0..1, ör. 0.20 = %20).</summary>
    public decimal KdvOrani { get; set; } = 0.20m;

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
