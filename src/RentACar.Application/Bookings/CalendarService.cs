using RentACar.Application.Authorization;
using RentACar.Domain.Common;

namespace RentACar.Application.Bookings;

/// <summary>
/// Rezervasyon takvimi (salt-okunur): bir tarih aralığındaki araç doluluğu. Operatör
/// yalnız atanmış şubesini görür (çıkış ofisi kapsamı). Yeni tablo/yazım YOK.
/// </summary>
public sealed class CalendarService(ICalendarRepository repository, ICurrentUser currentUser)
{
    private readonly ICalendarRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<OccupancySpanDto>> GetOccupancyAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => _repository.GetOccupancyAsync(from, to, BranchScope.Effective(_currentUser), ct);
}
