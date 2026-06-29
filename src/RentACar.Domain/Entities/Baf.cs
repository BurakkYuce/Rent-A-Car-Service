using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// BAF — personele araç tahsis kaydı (roadmap L5): çıkış (km/yakıt/şube) → dönüş (km/yakıt). Bir personele
/// şirket aracı zimmetleme/teslim. NOT: clone'daki DamageFile (hasar dosyası) ile karıştırılmamalı — bu
/// personel araç tahsisidir. Tenant-owned + auditable; full-CRUD. DEFTER POSTLAMAZ (zimmet kaydı).
/// </summary>
public class Baf : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (BAF-000001).</summary>
    public string No { get; set; } = string.Empty;

    public Guid PersonelId { get; set; }
    public Guid VehicleId { get; set; }

    public DateTimeOffset CikisTarihi { get; set; } = DateTimeOffset.UtcNow;
    public int CikisKm { get; set; }
    public int? CikisYakit { get; set; }

    public DateTimeOffset? DonusTarihi { get; set; }
    public int? DonusKm { get; set; }
    public int? DonusYakit { get; set; }

    public string? Sube { get; set; }
    public BafDurum Durum { get; set; } = BafDurum.Acik;
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
