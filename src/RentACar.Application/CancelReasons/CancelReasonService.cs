using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.CancelReasons;

/// <summary>
/// İptal sebebi master tanımı — <see cref="MasterDefinitionService{T}"/> ince alt sınıfı (O12d): doğrulama,
/// kod benzersizliği, CRUD, OperationsWrite guard ve liste cache ("cancel-reasons") tabandan gelir;
/// burada yalnız <see cref="CancelReasonInput"/> (kod, ad, aktif) üçlüsüne açılır. Dış yüzey değişmedi.
/// </summary>
public sealed class CancelReasonService(ICancelReasonRepository repository, ICurrentUser currentUser, ITenantCache cache)
    : MasterDefinitionService<CancelReason>(repository, currentUser, cache, "cancel-reasons", "iptal sebebi")
{
    public Task<Guid> CreateAsync(CancelReasonInput input, CancellationToken ct = default)
        => CreateCoreAsync(input.Kod, input.Ad, input.Aktif, ct);

    public Task<bool> UpdateAsync(Guid id, CancelReasonInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, ct);

    /// <summary>F11.1a — full replacement with optimistic concurrency (409 <c>cakisma</c> on a stale version).</summary>
    public Task<bool> UpdateAsync(Guid id, CancelReasonInput input, string expectedVersion, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, expectedVersion, ct);
}
