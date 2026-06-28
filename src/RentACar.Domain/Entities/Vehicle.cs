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

    /// <summary>Model/tip (ör. Egea, Clio).</summary>
    public string? Tip { get; set; }

    /// <summary>Araç grubu / fiyat sınıfı (rate-class).</summary>
    public string? Grup { get; set; }

    /// <summary>Segment (ör. Ekonomik, Orta, SUV).</summary>
    public string? Segment { get; set; }

    /// <summary>SIPP/ACRISS kodu (4 harf, ör. CDMD).</summary>
    public string? Sipp { get; set; }

    public string? Renk { get; set; }

    /// <summary>Model yılı (opsiyonel).</summary>
    public int? ModelYili { get; set; }

    public Vites? Vites { get; set; }

    public string? SasiNo { get; set; }

    public string? MotorNo { get; set; }

    /// <summary>İşlem şubesi.</summary>
    public string? Sube { get; set; }

    /// <summary>Operasyonel durum (Boş/Kirada/Serviste…).</summary>
    public VehicleStatus Durum { get; set; } = VehicleStatus.Stokta;

    /// <summary>Filo yaşam döngüsü statüsü (stok/havuz/tahsis…); operasyonel <see cref="Durum"/>'dan ayrı.</summary>
    public FiloStatus? FiloDurum { get; set; }

    public int Km { get; set; }

    public FuelType Yakit { get; set; } = FuelType.Benzin;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
