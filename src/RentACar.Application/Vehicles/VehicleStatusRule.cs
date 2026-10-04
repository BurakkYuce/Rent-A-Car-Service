using RentACar.Application.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.Vehicles;

/// <summary>Aracın durumunu belirleyen açık kayıtlar (belge numaraları; yoksa <c>null</c>).</summary>
/// <param name="OpenRentalNo">Açık (<see cref="RentalStatus.Kirada"/>) kira sözleşmesi.</param>
/// <param name="ActiveServiceNo">Serviste (<see cref="ServiceStatus.Serviste"/>) servis kaydı.</param>
/// <param name="OpenBafNo">Açık (<see cref="BafStatus.Acik"/>) personel tahsisi.</param>
/// <param name="CompletedSaleNo">Tamamlanmış araç satışı.</param>
public sealed record VehicleCommitments(string? OpenRentalNo, string? ActiveServiceNo, string? OpenBafNo, string? CompletedSaleNo)
{
    public static readonly VehicleCommitments None = new(null, null, null, null);
}

/// <summary>
/// Araç kartında durumun ELLE değiştirilmesi kuralı (kabul bulgusu: kiradaki araç kartta "Müsait" yapılabiliyordu →
/// çift kiralama yolu). Kirada/Serviste durumları yalnız kendi akışlarıyla değişir (kira teslim/dönüş/iptal, servis
/// başla/tamamla/iptal, araç satışı). Kart yalnız Müsait/Pasif/Satıldı arasında ve araca bağlı açık kayıt yokken geçiş yapar.
/// <list type="table">
/// <item>Durum aynı → serbest (kartın diğer alanları düzenlenebilir).</item>
/// <item>→ Kirada / → Serviste → her zaman red.</item>
/// <item>→ Müsait/Pasif/Satıldı → açık kira, serviste kayıt ya da açık tahsis varsa red.</item>
/// <item>Satıldı → başka durum → tamamlanmış satış kaydı varsa red (satış defterle çelişirdi).</item>
/// </list>
/// Saf fonksiyon: açık kayıtlar çağırandan gelir (<see cref="IVehicleRepository.CommitmentsAsync"/>).
/// </summary>
public static class VehicleStatusRule
{
    public static void EnsureManualChange(VehicleStatus current, VehicleStatus requested, VehicleCommitments c)
    {
        if (requested == current) return;
        if (requested == VehicleStatus.Kirada)
            throw new ValidationException("Araç durumu elle 'Kirada' yapılamaz; kira sözleşmesinde teslimle değişir.", "durum");
        if (requested == VehicleStatus.Serviste)
            throw new ValidationException("Araç durumu elle 'Serviste' yapılamaz; servis kaydında 'Servise Başla' ile değişir.", "durum");
        if (c.OpenRentalNo is { } r)
            throw new ValidationException($"Aracın açık kira sözleşmesi var ({r}); durumu kira dönüşü ya da iptaliyle değişir.", "durum");
        if (c.ActiveServiceNo is { } s)
            throw new ValidationException($"Araç serviste ({s}); durumu servis tamamlanınca ya da iptal edilince değişir.", "durum");
        if (c.OpenBafNo is { } b)
            throw new ValidationException($"Aracın açık tahsisi var ({b}); önce tahsisi teslim alın ya da iptal edin.", "durum");
        if (current == VehicleStatus.Satildi && c.CompletedSaleNo is { } sale)
            throw new ValidationException($"Araç satış kaydıyla satılmış ({sale}); durumu kart üzerinden değiştirilemez.", "durum");
    }
}
