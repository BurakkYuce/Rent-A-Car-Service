using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Periyodik bakım tanım tablosu (roadmap N1): araç tipi → periyodik bakım KM aralığı. Periyodik servis
/// raporunun (H1) zemini. Tenant-owned + auditable; full-CRUD basit master (Kod benzersiz).
/// </summary>
public class ServisTanim : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string AracTipi { get; set; } = string.Empty;
    public int BakimKm { get; set; }
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
