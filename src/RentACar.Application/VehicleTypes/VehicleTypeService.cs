using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleTypes;

/// <summary>
/// Araç tip master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class VehicleTypeService(IVehicleTypeRepository repository, ICurrentUser currentUser)
{
    private readonly IVehicleTypeRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<VehicleType>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<VehicleType>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<VehicleType?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(VehicleTypeInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu araç tipi zaten var.");

        var type = new VehicleType();
        Apply(type, n);
        await _repository.CreateAsync(type, ct);
        return type.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, VehicleTypeInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu araç tipi zaten var.");

        return await _repository.UpdateAsync(id, type =>
        {
            Apply(type, n);
            type.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(VehicleTypeInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Araç tipi kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Araç tipi kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Araç tipi adı zorunludur.");
    }

    private static VehicleTypeInput Normalize(VehicleTypeInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Marka = string.IsNullOrWhiteSpace(input.Marka) ? null : input.Marka.Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(VehicleType type, VehicleTypeInput n)
    {
        type.Kod = n.Kod;
        type.Ad = n.Ad;
        type.Marka = n.Marka;
        type.Aktif = n.Aktif;
    }
}
