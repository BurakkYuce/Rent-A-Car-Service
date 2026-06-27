namespace RentACar.Application.Bookings;

/// <summary>
/// Takvim doluluk çubuğu: bir araç için bir tarih aralığında rezervasyon veya kira.
/// Salt-okunur (Reservations + Rentals üstünde). Tip: "Rezervasyon" | "Kira".
/// </summary>
public sealed record OccupancySpanDto(
    Guid VehicleId, string No, string Tip, string Durum, DateTimeOffset Bas, DateTimeOffset Bit);
