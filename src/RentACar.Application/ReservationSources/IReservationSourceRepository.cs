using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.ReservationSources;

/// <summary>Rezervasyon kaynağı repo sözleşmesi — üye setinin çoğu <see cref="IMasterTanimRepository{T}"/>'den gelir.</summary>
public interface IReservationSourceRepository : IMasterTanimRepository<ReservationSource>
{
    /// <summary>
    /// FAZ-24 "Aşağıya Yansıt": verilen oranları <paramref name="haricId"/> dışındaki AKTİF
    /// kaynaklara yazar (tek transaction). Yalnız bu tabloya dokunur.
    /// </summary>
    /// <returns>Güncellenen satır sayısı.</returns>
    Task<int> OranlariYansitAsync(Guid haricId, decimal? kiraOrani, decimal? hizmetOrani,
        decimal? dropOrani, CancellationToken ct = default);
}
