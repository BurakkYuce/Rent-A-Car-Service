using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç — PR #1 dikey diliminin çekirdek varlığı. Orijinaldeki 80+ alanın yalnız
/// çekirdeği modellenir (plaka, marka, grup, şube, durum, km, yakıt). Tenant-owned
/// + auditable: EF global query filter + Postgres RLS ile izole, değişiklikleri
/// AuditLog'a yazılır.
/// </summary>
public class Vehicle : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Plaka — zorunlu; tenant içinde benzersiz (doğal iş anahtarı).</summary>
    public string Plaka { get; set; } = string.Empty;

    public string? Marka { get; set; }

    /// <summary>Araç grubu / fiyat sınıfı (rate-class).</summary>
    public string? Grup { get; set; }

    /// <summary>İşlem şubesi.</summary>
    public string? Sube { get; set; }

    public VehicleStatus Durum { get; set; } = VehicleStatus.Stokta;

    public int Km { get; set; }

    public FuelType Yakit { get; set; } = FuelType.Benzin;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
