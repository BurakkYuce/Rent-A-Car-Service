using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Lokasyon-şube drop matrisi (roadmap N2): bir lokasyon × şube ikilisi için karşılama/çalışma şekli +
/// özel iletişim. Aracı bir lokasyonda alıp başka şubeye bırakma (drop) konfigürasyonu. Tenant-owned +
/// auditable; full-CRUD basit master. Benzersiz: (TenantId, Lokasyon, Sube).
/// </summary>
public class DropTanim : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Lokasyon { get; set; } = string.Empty;
    public string Sube { get; set; } = string.Empty;
    public string? KarsilamaSekli { get; set; }
    public string? CalismaSekli { get; set; }
    public string? OzelIletisim { get; set; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
