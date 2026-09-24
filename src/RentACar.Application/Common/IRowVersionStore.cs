namespace RentACar.Application.Common;

/// <summary>
/// F9.1 — optimistic concurrency port for full-replacement PUTs of <c>/api/ui</c> (servis, sigorta, fiyat/tarife
/// tanımları): opaque row version (Postgres <c>xmin</c>) + versioned update under a row lock (<c>FOR UPDATE</c>).
/// Generic over the entity type: the table name comes from the EF model, never from input, so a new table needs no
/// change here. Services receive it as an OPTIONAL constructor dependency (existing callers keep compiling) and use
/// it only in their <c>UpdateVersionedAsync</c> overloads; the Blazor update paths are unchanged.
/// </summary>
public interface IRowVersionStore
{
    /// <summary>Opaque version of one row; <c>null</c> when the row is missing or outside the tenant (RLS).</summary>
    Task<string?> GetVersionAsync<T>(Guid id, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Row lock + version comparison + <paramref name="apply"/> in ONE transaction. Different version →
    /// <see cref="EszamanliDegisiklikException"/> (409 <c>cakisma</c>), nothing is written. Missing row → <c>false</c>.
    /// A unique violation (natural key raced by another writer) becomes <see cref="ValidationException"/> with
    /// <paramref name="duplicateMessage"/>.
    /// </summary>
    Task<bool> UpdateAsync<T>(Guid id, string expectedVersion, Action<T> apply, string duplicateMessage,
        CancellationToken ct = default) where T : class;
}

/// <summary>Shared guard for the optional <see cref="IRowVersionStore"/> dependency.</summary>
public static class RowVersionStoreGuard
{
    public static IRowVersionStore Require(IRowVersionStore? store)
        => store ?? throw new InvalidOperationException("IRowVersionStore is not registered (versioned update unavailable).");
}
