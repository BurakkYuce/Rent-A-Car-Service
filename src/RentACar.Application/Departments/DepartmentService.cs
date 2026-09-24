using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Departments;

/// <summary>
/// Departman master tanımı — <see cref="MasterTanimService{T}"/> ince alt sınıfı (O12d): doğrulama,
/// kod benzersizliği, CRUD, OperationsWrite guard ve liste cache ("departments") tabandan gelir;
/// burada yalnız <see cref="DepartmentInput"/> (kod, ad, aktif) üçlüsüne açılır. Dış yüzey değişmedi.
/// </summary>
public sealed class DepartmentService(IDepartmentRepository repository, ICurrentUser currentUser, ITenantCache cache)
    : MasterTanimService<Department>(repository, currentUser, cache, "departments", "departman")
{
    public Task<Guid> CreateAsync(DepartmentInput input, CancellationToken ct = default)
        => CreateCoreAsync(input.Kod, input.Ad, input.Aktif, ct);

    public Task<bool> UpdateAsync(Guid id, DepartmentInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, ct);

    /// <summary>F11.1a — full replacement with optimistic concurrency (409 <c>cakisma</c> on a stale version).</summary>
    public Task<bool> UpdateAsync(Guid id, DepartmentInput input, string expectedVersion, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, expectedVersion, ct);
}
