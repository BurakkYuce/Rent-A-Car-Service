using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.ExpenseCategories;

/// <summary>
/// Gider türü master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class ExpenseCategoryService(IExpenseCategoryRepository repository, ICurrentUser currentUser)
{
    private readonly IExpenseCategoryRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<ExpenseCategory>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<ExpenseCategory>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<ExpenseCategory?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(ExpenseCategoryInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu gider türü zaten var.");

        var category = new ExpenseCategory();
        Apply(category, n);
        await _repository.CreateAsync(category, ct);
        return category.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, ExpenseCategoryInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu gider türü zaten var.");

        return await _repository.UpdateAsync(id, category =>
        {
            Apply(category, n);
            category.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(ExpenseCategoryInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Gider türü kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Gider türü kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Gider türü adı zorunludur.");
    }

    private static ExpenseCategoryInput Normalize(ExpenseCategoryInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(ExpenseCategory category, ExpenseCategoryInput n)
    {
        category.Kod = n.Kod;
        category.Ad = n.Ad;
        category.Aktif = n.Aktif;
    }
}
