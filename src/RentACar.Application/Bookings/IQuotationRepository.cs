using RentACar.Domain.Entities;

namespace RentACar.Application.Bookings;

public interface IQuotationRepository
{
    /// <summary>Teklifler (liste). Şube kapsamı (çıkış ofisi) ile sınırlanabilir.</summary>
    Task<IReadOnlyList<Quotation>> ListAsync(string? sube = null, CancellationToken ct = default);

    Task<Quotation?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Boşluksuz No (TK-000001) tahsis ederek oluşturur.</summary>
    Task CreateAsync(Quotation quotation, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<Quotation> apply, CancellationToken ct = default);

    /// <summary>
    /// Teklifi rezervasyona çevirir (tek transaction): ReservationNo tahsis + Reservation
    /// insert + teklif Durum/ReservationId güncelle. Oluşan rezervasyon Id döner.
    /// </summary>
    Task<Guid> ConvertToReservationAsync(
        Guid quotationId, Func<Quotation, Reservation> buildReservation, CancellationToken ct = default);
}
