using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.TransmissionTypes;

/// <summary>
/// Vites türü master tanımı — <see cref="MasterTanimService{T}"/> ince alt sınıfı (O12d): doğrulama,
/// kod benzersizliği, CRUD, OperationsWrite guard ve liste cache ("transmission-types") tabandan gelir;
/// burada yalnız <see cref="TransmissionTypeInput"/> (kod, ad, aktif) üçlüsüne açılır. Dış yüzey değişmedi.
/// </summary>
public sealed class TransmissionTypeService(ITransmissionTypeRepository repository, ICurrentUser currentUser, ITenantCache cache)
    : MasterTanimService<TransmissionType>(repository, currentUser, cache, "transmission-types", "vites türü")
{
    public Task<Guid> CreateAsync(TransmissionTypeInput input, CancellationToken ct = default)
        => CreateCoreAsync(input.Kod, input.Ad, input.Aktif, ct);

    public Task<bool> UpdateAsync(Guid id, TransmissionTypeInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, ct);
}
