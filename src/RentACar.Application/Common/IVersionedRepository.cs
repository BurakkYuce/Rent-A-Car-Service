namespace RentACar.Application.Common;

/// <summary>
/// F11.1a — optimistic concurrency port of a definition repository (full-replacement PUT of <c>/api/ui</c>):
/// opaque row version (Postgres <c>xmin</c>) + versioned update under a row lock. Implementations use the generic
/// <c>Infrastructure.Persistence.RowVersion</c> helper, so a repository only forwards three calls.
/// </summary>
public interface IVersionedRepository<T> where T : class
{
    /// <summary>Opaque row version; null when the row is missing or outside the tenant.</summary>
    Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default);

    /// <summary>Versions of all visible rows (list responses carry the version).</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default);

    /// <summary>Row lock + version comparison + apply in one transaction; mismatch →
    /// <see cref="ConcurrentModificationException"/>; missing row → false.</summary>
    Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<T> apply, CancellationToken ct = default);
}
