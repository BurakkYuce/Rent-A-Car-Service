using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç satışı. Tenant-owned + auditable + DB-immutable (mali belge). Satış kesilince
/// alıcı cari BORÇLANIR: Borç Cari (brüt) / Alacak Gelir (net) / Alacak KDV (kdv) — dengeli.
/// Araç durumu Satildi'ye geçer (filodan çıkar). Düzeltme = ters kayıt (bu PR'da kapsam dışı).
/// </summary>
public class VehicleSale : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (ST-000001).</summary>
    public string No { get; set; } = string.Empty;

    public Guid VehicleId { get; set; }
    public Guid AliciCariId { get; set; }   // satın alan müşteri (cari)

    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;
    public string? NoterNo { get; set; }

    public decimal SatisNet { get; set; }
    public decimal KdvOrani { get; set; }
    public decimal KdvTutar { get; set; }
    public decimal GenelToplam { get; set; }
    public string Currency { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;

    public string? Aciklama { get; set; }
    public SatisDurum Durum { get; set; } = SatisDurum.Tamamlandi;

    // roadmap G2 (additive — bilgilendirme; deftere yansımaz)
    public decimal? HedefFiyat { get; set; }
    public int? SatisKm { get; set; }
    public string? SatisKanali { get; set; }
    public string? Devir { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
