using RentACar.Domain.Entities;

namespace RentACar.Application.GelenEFaturalar;

public interface IGelenEFaturaRepository
{
    /// <summary>Filtreli liste (FAZ-55). <paramref name="filter"/> null → tüm kayıtlar.</summary>
    Task<IReadOnlyList<GelenEFatura>> ListAsync(GelenEFaturaFilter? filter, CancellationToken ct = default);
    Task<GelenEFatura?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> EttnExistsAsync(string ettn, CancellationToken ct = default);
    Task CreateAsync(GelenEFatura row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<GelenEFatura> apply, CancellationToken ct = default);
}
