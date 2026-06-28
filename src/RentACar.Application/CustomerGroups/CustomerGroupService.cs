using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.CustomerGroups;

/// <summary>
/// Müşteri grubu master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class CustomerGroupService(ICustomerGroupRepository repository, ICurrentUser currentUser)
{
    private readonly ICustomerGroupRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<CustomerGroup>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<CustomerGroup>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<CustomerGroup?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(CustomerGroupInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu müşteri grubu zaten var.");

        var group = new CustomerGroup();
        Apply(group, n);
        await _repository.CreateAsync(group, ct);
        return group.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, CustomerGroupInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu müşteri grubu zaten var.");

        return await _repository.UpdateAsync(id, group =>
        {
            Apply(group, n);
            group.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(CustomerGroupInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Müşteri grubu kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Müşteri grubu kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Müşteri grubu adı zorunludur.");
    }

    private static CustomerGroupInput Normalize(CustomerGroupInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(CustomerGroup group, CustomerGroupInput n)
    {
        group.Kod = n.Kod;
        group.Ad = n.Ad;
        group.Aktif = n.Aktif;
    }
}
