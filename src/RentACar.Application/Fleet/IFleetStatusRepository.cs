namespace RentACar.Application.Fleet;

public interface IFleetStatusRepository
{
    /// <summary>Filtreye uyan araçları aktif kira özetiyle birleştirip döndürür (plaka sıralı).</summary>
    Task<IReadOnlyList<FleetStatusRow>> QueryAsync(FleetStatusFilter filter, CancellationToken ct = default);
}
