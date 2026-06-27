namespace RentACar.Application.Bookings;

public interface ICalendarRepository
{
    /// <summary>
    /// [from,to) ile kesişen doluluk çubukları: aktif rezervasyonlar (Rezerv/Onaylı) +
    /// aktif kiralar (Kirada). Şube (çıkış ofisi) ile sınırlanabilir.
    /// </summary>
    Task<IReadOnlyList<OccupancySpanDto>> GetOccupancyAsync(
        DateTimeOffset from, DateTimeOffset to, string? sube = null, CancellationToken ct = default);
}
