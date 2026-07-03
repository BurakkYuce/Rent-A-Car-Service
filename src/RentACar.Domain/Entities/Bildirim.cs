using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Kalıcı uygulama-içi bildirim (roadmap: scheduler+vade uyarısı). Arka-plan iş (VadeBildirimJob)
/// yaklaşan/geçmiş sigorta/MTV/muayene vadelerini tarayıp buraya yazar; kullanıcı okundu işaretler.
/// Tenant-owned (RLS). İdempotency: kaynak başına tek bildirim — kısmi-unique (TenantId, Tur,
/// VehicleId, VadeTarihi). Dış entegrasyon/e-posta YOK (kimliksiz).
/// </summary>
public class Bildirim : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kaynak türü: "Kasko" / "Trafik" / "MTV" / "Muayene" (VadeSource.Tur).</summary>
    public string Tur { get; set; } = string.Empty;
    public Guid VehicleId { get; set; }
    public DateTimeOffset VadeTarihi { get; set; }
    public string Mesaj { get; set; } = string.Empty;
    public bool Okundu { get; set; }
    public DateTimeOffset OlusturmaTarihi { get; set; } = DateTimeOffset.UtcNow;
}
