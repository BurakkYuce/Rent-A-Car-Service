using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Uzun-dönem (filo) kiralama sözleşmesi (roadmap L1): aylık ücretli, çok-aylık süreli operasyonel kiralama.
/// Günlük <see cref="RentalContract"/>'tan AYRI. Bu kayıt sözleşme şartları + taksit planının kaynağıdır;
/// DEFTER POSTLAMAZ — çok-aylık gelir peşin tanınmaz, gelir aylık faturalama (manuel fatura) ile tanınır.
/// Tenant-owned + auditable; full-CRUD (mali değişmez belge DEĞİL).
/// </summary>
public class FiloKiralama : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (FK-000001).</summary>
    public string No { get; set; } = string.Empty;

    public Guid MusteriId { get; set; }
    public Guid VehicleId { get; set; }

    public DateTimeOffset BasTar { get; set; } = DateTimeOffset.UtcNow;
    public int SureAy { get; set; }
    public decimal AylikUcret { get; set; }
    public decimal KdvOrani { get; set; } = 0.20m; // kesir (0.20 = %20)
    public string Currency { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;

    public int? ToplamKmLimiti { get; set; }
    public decimal? DamgaVergisi { get; set; }

    public FiloKiraDurum Durum { get; set; } = FiloKiraDurum.Aktif;
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
