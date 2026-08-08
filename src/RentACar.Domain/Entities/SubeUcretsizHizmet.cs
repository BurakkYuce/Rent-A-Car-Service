using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Şubeye özel ÜCRETSİZ hizmet (canlı sube_tanimlama.aspx child listesi): "havalimanı teslim",
/// "bebek koltuğu ücretsiz" gibi o şubede ek ücret alınmayan kalemler.
///
/// <para>PARA TAŞIMAZ ve fiyat motoruna GİRMEZ — ücretli ek hizmet <c>EkHizmetTanim</c>/
/// <c>RentalAddOn</c>'dur. Burası vitrin/bilgi amaçlı bir listedir.</para>
/// </summary>
public class SubeUcretsizHizmet : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid SubeId { get; set; }
    public string HizmetAdi { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
