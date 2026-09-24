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

    /// <summary>#286 M3 — satır sürümü (opak; Postgres xmin). Yoksa null.</summary>
    Task<string?> VersionAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// #286 M3/Low-9 — kilitli güncelleme: satır <c>FOR UPDATE</c>; <paramref name="expectedVersion"/> verilirse kilit
    /// altında karşılaştırılır (farklıysa <c>EszamanliDegisiklikException</c>, 409 <c>cakisma</c>). <paramref name="apply"/>
    /// kilit altındaki GÜNCEL satırla çağrılır. Kayıt yoksa false.
    /// </summary>
    Task<bool> UpdateLockedAsync(Guid id, string? expectedVersion, Action<GelenEFatura> apply, CancellationToken ct = default);
}
