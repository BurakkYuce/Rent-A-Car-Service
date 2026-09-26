using Microsoft.EntityFrameworkCore;
using RentACar.Application.Bookings;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Takvim doluluğu (salt-okunur). Rezervasyon (Rezerv/Onaylı) + kira (Kirada) kayıtlarından
/// [from,to) ile kesişenleri tek listeye projeksiyon. Tenant izolasyonu RLS + query filter.
/// Kesişim: span.Bas &lt; to AND from &lt; span.Bit (yarı-açık aralık).
/// </summary>
public sealed class CalendarRepository(IDbContextFactory<AppDbContext> factory) : ICalendarRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<OccupancySpanDto>> GetOccupancyAsync(
        DateTimeOffset from, DateTimeOffset to, string? branch = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var res = db.Reservations.AsNoTracking()
            .Where(r => r.Durum == ReservationStatus.Rezerv || r.Durum == ReservationStatus.Onayli)
            .Where(r => r.BasTar < to && from < r.BitTar);
        if (!string.IsNullOrWhiteSpace(branch)) res = res.Where(r => r.CikisOfisi == branch);
        var resSpans = await res
            .Select(r => new OccupancySpanDto(
                r.VehicleId, r.ReservationNo, "Rezervasyon", r.Durum.ToString(), r.BasTar, r.BitTar))
            .ToListAsync(ct);

        var rental = db.Rentals.AsNoTracking()
            .Where(r => r.Durum == RentalStatus.Kirada)
            .Where(r => r.BasTar < to && from < r.BitTar);
        if (!string.IsNullOrWhiteSpace(branch)) rental = rental.Where(r => r.CikisOfisi == branch);
        var rentalSpans = await rental
            .Select(r => new OccupancySpanDto(
                r.VehicleId, r.SozlesmeNo, "Kira", r.Durum.ToString(), r.BasTar, r.BitTar))
            .ToListAsync(ct);

        return [.. resSpans, .. rentalSpans];
    }
}
