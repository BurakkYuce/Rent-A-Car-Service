using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Ofis/Lokasyon (alış-dönüş noktası) master kaydı. Tenant-owned + auditable. Şube'den AYRI:
/// bir şubede birden çok ofis olabilir (havalimanı, şehir merkezi…). Additive — rezervasyon/
/// teklif/kira formlarındaki serbest-metin <c>CikisOfisi</c>/<c>DonusOfisi</c> alanlarını
/// besleyen açılır liste kaynağıdır (FK değil; seçilen <see cref="Ad"/> metni yazılır).
/// </summary>
public class Location : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;

    public string? Adres { get; set; }
    public string? Telefon { get; set; }

    /// <summary>Opsiyonel şube bağı (serbest metin — geriye-uyum; görüntü/yedek).</summary>
    public string? Sube { get; set; }
    /// <summary>Şube FK (Branch master, roadmap F1; metin korunur).</summary>
    public Guid? SubeId { get; set; }

    /// <summary>Pasif ofisler açılır listelerde gizlenir ama kayıtlar korunur.</summary>
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
