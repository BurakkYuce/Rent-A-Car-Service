using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Countries;

/// <summary>
/// Ülke master tanımı — <see cref="MasterTanimService{T}"/> ince alt sınıfı (O12d): doğrulama,
/// kod benzersizliği, CRUD, OperationsWrite guard ve liste cache ("countries") tabandan gelir;
/// burada yalnız <see cref="CountryInput"/> (kod, ad, aktif) üçlüsüne açılır. Dış yüzey değişmedi.
/// </summary>
public sealed class CountryService(ICountryRepository repository, ICurrentUser currentUser, ITenantCache cache)
    : MasterTanimService<Country>(repository, currentUser, cache, "countries", "ülke")
{
    public Task<Guid> CreateAsync(CountryInput input, CancellationToken ct = default)
        => CreateCoreAsync(input.Kod, input.Ad, input.Aktif, ct);

    public Task<bool> UpdateAsync(Guid id, CountryInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, ct);
}
