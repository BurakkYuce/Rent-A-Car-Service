using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç grubu tanımı (master sözlük): araçların gruplandığı kısa adlandırılmış liste
/// (ör. "EKO" = Ekonomik, "SUV"). Tenant-owned + auditable. Bu BASİT sözlük sürümüdür; araç
/// kayıt/düzenleme formundaki serbest-metin Grup alanını açılır listeden besler. Vehicle.Grup
/// kolonu STRING kalır (FK YOK — additive). NOT: Canlı TürevRent'teki zengin "Araç Grubu Tanımı"
/// (SIPP/segment/KM limiti/muafiyet/sürücü yaşı = fiyat-kural master'ı) AYRI/daha büyük bir iştir.
/// </summary>
public class VehicleGroup : ITenantOwned, IAuditable
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
