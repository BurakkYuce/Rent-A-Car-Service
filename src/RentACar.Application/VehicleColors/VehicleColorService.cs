using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleColors;

/// <summary>
/// Renk master tanımı — <see cref="MasterTanimService{T}"/> ince alt sınıfı (O12d): doğrulama,
/// kod benzersizliği, CRUD, OperationsWrite guard ve liste cache ("vehicle-colors") tabandan gelir;
/// burada yalnız <see cref="VehicleColorInput"/> (kod, ad, aktif) üçlüsüne açılır. Dış yüzey değişmedi.
/// </summary>
public sealed class VehicleColorService(IVehicleColorRepository repository, ICurrentUser currentUser, ITenantCache cache)
    : MasterTanimService<VehicleColor>(repository, currentUser, cache, "vehicle-colors", "renk")
{
    public Task<Guid> CreateAsync(VehicleColorInput input, CancellationToken ct = default)
        => CreateCoreAsync(input.Kod, input.Ad, input.Aktif, ct);

    public Task<bool> UpdateAsync(Guid id, VehicleColorInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, ct);
}
