using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.FuelKinds;

/// <summary>
/// Yakıt türü master tanımı — <see cref="MasterTanimService{T}"/> ince alt sınıfı (O12d): doğrulama,
/// kod benzersizliği, CRUD, OperationsWrite guard ve liste cache ("fuel-kinds") tabandan gelir;
/// burada yalnız <see cref="FuelKindInput"/> (kod, ad, aktif) üçlüsüne açılır. Dış yüzey değişmedi.
/// </summary>
public sealed class FuelKindService(IFuelKindRepository repository, ICurrentUser currentUser, ITenantCache cache)
    : MasterTanimService<FuelKind>(repository, currentUser, cache, "fuel-kinds", "yakıt türü")
{
    public Task<Guid> CreateAsync(FuelKindInput input, CancellationToken ct = default)
        => CreateCoreAsync(input.Kod, input.Ad, input.Aktif, ct);

    public Task<bool> UpdateAsync(Guid id, FuelKindInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, ct);
}
