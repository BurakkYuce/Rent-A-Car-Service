using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Accessories;

/// <summary>
/// Aksesuar master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class AccessoryService(IAccessoryRepository repository, ICurrentUser currentUser)
{
    private readonly IAccessoryRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Accessory>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<Accessory>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<Accessory?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(AccessoryInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu aksesuar zaten var.");

        var accessory = new Accessory();
        Apply(accessory, n);
        await _repository.CreateAsync(accessory, ct);
        return accessory.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, AccessoryInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu aksesuar zaten var.");

        return await _repository.UpdateAsync(id, accessory =>
        {
            Apply(accessory, n);
            accessory.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(AccessoryInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Aksesuar kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Aksesuar kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Aksesuar adı zorunludur.");
    }

    private static AccessoryInput Normalize(AccessoryInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(Accessory accessory, AccessoryInput n)
    {
        accessory.Kod = n.Kod;
        accessory.Ad = n.Ad;
        accessory.Aciklama = n.Aciklama;
        accessory.Aktif = n.Aktif;
    }
}
