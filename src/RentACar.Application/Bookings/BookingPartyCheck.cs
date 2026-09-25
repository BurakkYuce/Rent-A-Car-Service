using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;

namespace RentACar.Application.Bookings;

/// <summary>
/// Kira/rezervasyon/teklif (ve filo kiralama) belgesinin müşteri ve aracı bu kiracıda VAR olmalı.
/// <c>Rentals/Reservations/Quotations.MusteriId/VehicleId</c>'de bileşik FK yok → hiç olmayan ya da BAŞKA
/// KİRACININ kimliğiyle belge yazılabiliyordu; teklif → rezervasyon → kira zinciri bu kimliği fatura/deftere
/// taşırdı. RLS + tenant query filter kapsamlı <c>FindAsync</c>: yabancı kiracının kaydı "yok" görünür (varlık
/// sızmaz). Şube süzgeci YOK (müşteri/araç şube kapsamlı değil). Mesaj ve alan harici API ile <c>/api/ui</c>'de
/// aynıdır (tek kaynak). Kalıcı çözüm (bileşik FK) ayrı iş.
/// </summary>
public static class BookingPartyCheck
{
    /// <summary>Önce müşteri, sonra araç denetlenir; bulunan müşteri döner (risk limiti gibi sonraki kurallar için).</summary>
    public static async Task<Customer> RequireAsync(
        ICustomerRepository customers, IVehicleRepository vehicles, Guid musteriId, Guid vehicleId, CancellationToken ct)
    {
        var musteri = (musteriId == Guid.Empty ? null : await customers.FindAsync(musteriId, ct))
            ?? throw new ValidationException("Müşteri bulunamadı.", "musteriId");
        if (vehicleId == Guid.Empty || await vehicles.FindAsync(vehicleId, ct) is null)
            throw new ValidationException("Araç bulunamadı.", "vehicleId");
        return musteri;
    }
}
