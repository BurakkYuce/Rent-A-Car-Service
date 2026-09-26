using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.CustomerGroups;

/// <summary>
/// Müşteri grubu master tanımı — <see cref="MasterDefinitionService{T}"/> ince alt sınıfı (O12d): doğrulama,
/// kod benzersizliği, CRUD, OperationsWrite guard ve liste cache ("customer-groups") tabandan gelir;
/// burada yalnız <see cref="CustomerGroupInput"/> (kod, ad, aktif) üçlüsüne açılır. Dış yüzey değişmedi.
/// </summary>
public sealed class CustomerGroupService(ICustomerGroupRepository repository, ICurrentUser currentUser, ITenantCache cache)
    : MasterDefinitionService<CustomerGroup>(repository, currentUser, cache, "customer-groups", "müşteri grubu")
{
    public Task<Guid> CreateAsync(CustomerGroupInput input, CancellationToken ct = default)
        => CreateCoreAsync(input.Kod, input.Ad, input.Aktif, ct);

    public Task<bool> UpdateAsync(Guid id, CustomerGroupInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, ct);

    /// <summary>F11.1a — full replacement with optimistic concurrency (409 <c>cakisma</c> on a stale version).</summary>
    public Task<bool> UpdateAsync(Guid id, CustomerGroupInput input, string expectedVersion, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, expectedVersion, ct);
}
