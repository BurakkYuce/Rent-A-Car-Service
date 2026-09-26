namespace RentACar.Domain.Enums;

/// <summary>
/// Aracın operasyonel durumu: Boşta(Musait)/Kirada/Serviste/Pasif/Satıldı.
/// NOT: eski "Stokta" (=0) operasyonel değeri KALDIRILDI — Musait ("boşta") ile mükerrer olup
/// kullanıcının kafasını karıştırıyordu. Aracın filo/tedarik "stok" durumu AYRI kavramdır:
/// <see cref="FleetLifecycleStatus.SifirKmStok"/> (0KM stok) — bilinçli olarak oraya girilir, otomatik atanmaz.
/// Sayı değerleri korundu (Musait=1…) ki mevcut DB satırları bozulmasın.
/// </summary>
public enum VehicleStatus
{
    Musait = 1,
    Kirada = 2,
    Serviste = 3,
    Pasif = 4,
    Satildi = 5
}
