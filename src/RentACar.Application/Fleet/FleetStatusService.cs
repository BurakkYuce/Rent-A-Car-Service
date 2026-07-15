using RentACar.Application.Authorization;
using RentACar.Domain.Common;

namespace RentACar.Application.Fleet;

/// <summary>
/// Araç Güncel Durum (operasyon kalbi): araç + aktif kira birleşik görünümü. Salt-okunur.
/// Rol bazlı ŞUBE kapsamı zorlanır (operatör yalnız kendi şubesi) — VehicleService ile aynı kural.
/// </summary>
public sealed class FleetStatusService(IFleetStatusRepository repository, ICurrentUser currentUser)
{
    private readonly IFleetStatusRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<FleetStatusRow>> QueryAsync(FleetStatusFilter filter, CancellationToken ct = default)
    {
        filter.Kapsam = BranchScope.EffectiveFilter(_currentUser); // C3: FK-farkındalı kapsam
        return _repository.QueryAsync(filter, ct);
    }
}
