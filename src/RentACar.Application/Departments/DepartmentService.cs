using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Departments;

/// <summary>
/// Departman master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class DepartmentService(IDepartmentRepository repository, ICurrentUser currentUser)
{
    private readonly IDepartmentRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Department>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<Department>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<Department?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(DepartmentInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu departman zaten var.");

        var department = new Department();
        Apply(department, n);
        await _repository.CreateAsync(department, ct);
        return department.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, DepartmentInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu departman zaten var.");

        return await _repository.UpdateAsync(id, department =>
        {
            Apply(department, n);
            department.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(DepartmentInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Departman kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Departman kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Departman adı zorunludur.");
    }

    private static DepartmentInput Normalize(DepartmentInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(Department department, DepartmentInput n)
    {
        department.Kod = n.Kod;
        department.Ad = n.Ad;
        department.Aktif = n.Aktif;
    }
}
