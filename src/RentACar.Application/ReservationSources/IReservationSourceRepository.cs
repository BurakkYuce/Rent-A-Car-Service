using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.ReservationSources;

/// <summary>Rezervasyon kaynağı repo sözleşmesi — üye setinin çoğu <see cref="IMasterDefinitionRepository{T}"/>'den gelir.</summary>
public interface IReservationSourceRepository : IMasterDefinitionRepository<ReservationSource>
{
    /// <summary>
    /// FAZ-24 "Aşağıya Yansıt": verilen oranları <paramref name="excludedId"/> dışındaki AKTİF
    /// kaynaklara yazar (tek transaction). Yalnız bu tabloya dokunur.
    /// </summary>
    /// <returns>Güncellenen satır sayısı.</returns>
    Task<int> ReflectRatesAsync(Guid excludedId, decimal? rentalRate, decimal? serviceRate,
        decimal? dropRate, CancellationToken ct = default);
}
