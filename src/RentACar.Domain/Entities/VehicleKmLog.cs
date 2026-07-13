using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç km zaman serisi (FAZ 2.5). Operasyonel iz (mali belge değil): kira dönüşü (DonusKm) ve
/// servis çıkışı (CikisKm) kaynağıyla AYNI transaction'da otomatik yazılır; manuel giriş araç
/// kartından (Vehicle.Km de güncellenir, geriye gitme reddi). SALT-EKLEME: racar_app'e yalnız
/// SELECT+INSERT grant'i verilir — seri geriye dönük oynanamaz. Karnedeki "dönem km" bu seriden.
/// </summary>
public class VehicleKmLog : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid VehicleId { get; set; }
    public DateTimeOffset Tarih { get; set; }
    public int Km { get; set; }
    public KmLogKaynak Kaynak { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
