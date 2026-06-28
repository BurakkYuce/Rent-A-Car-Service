using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Hukuk dosyası (roadmap C2; canlı "hukuk" karşılığı). Master kayıt; doğal anahtar = DosyaNo.
/// Dava/icra takibi (opsiyonel cari ilişkisi). Tutar bilgilendirme amaçlı (deftere postlamaz).
/// </summary>
public class HukukDosya : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string DosyaNo { get; set; } = string.Empty;
    public Guid? CariId { get; set; }
    public HukukTuru Tur { get; set; } = HukukTuru.Dava;
    public string? Avukat { get; set; }
    public decimal Tutar { get; set; }
    public HukukDurum Durum { get; set; } = HukukDurum.Acik;
    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
