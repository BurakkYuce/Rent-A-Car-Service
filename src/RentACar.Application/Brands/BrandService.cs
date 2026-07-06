using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Brands;

/// <summary>
/// Marka master tanımı — <see cref="MasterTanimService{T}"/> ince alt sınıfı (O12d): doğrulama,
/// kod benzersizliği, CRUD, OperationsWrite guard ve liste cache ("brands") tabandan gelir;
/// burada yalnız <see cref="BrandInput"/> (kod, ad, aktif) üçlüsüne açılır. Dış yüzey değişmedi.
/// </summary>
public sealed class BrandService(IBrandRepository repository, ICurrentUser currentUser, ITenantCache cache)
    : MasterTanimService<Brand>(repository, currentUser, cache, "brands", "marka")
{
    public Task<Guid> CreateAsync(BrandInput input, CancellationToken ct = default)
        => CreateCoreAsync(input.Kod, input.Ad, input.Aktif, ct);

    public Task<bool> UpdateAsync(Guid id, BrandInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, ct);
}
