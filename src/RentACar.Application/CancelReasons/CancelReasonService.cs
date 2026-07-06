using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.CancelReasons;

/// <summary>
/// İptal sebebi master tanımı — <see cref="MasterTanimService{T}"/> ince alt sınıfı (O12d): doğrulama,
/// kod benzersizliği, CRUD, OperationsWrite guard ve liste cache ("cancel-reasons") tabandan gelir;
/// burada yalnız <see cref="CancelReasonInput"/> (kod, ad, aktif) üçlüsüne açılır. Dış yüzey değişmedi.
/// </summary>
public sealed class CancelReasonService(ICancelReasonRepository repository, ICurrentUser currentUser, ITenantCache cache)
    : MasterTanimService<CancelReason>(repository, currentUser, cache, "cancel-reasons", "iptal sebebi")
{
    public Task<Guid> CreateAsync(CancelReasonInput input, CancellationToken ct = default)
        => CreateCoreAsync(input.Kod, input.Ad, input.Aktif, ct);

    public Task<bool> UpdateAsync(Guid id, CancelReasonInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, ct);
}
