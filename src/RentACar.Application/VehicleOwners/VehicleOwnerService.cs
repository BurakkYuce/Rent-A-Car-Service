using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleOwners;

/// <summary>
/// Araç sahip master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class VehicleOwnerService(IVehicleOwnerRepository repository, ICurrentUser currentUser)
{
    private readonly IVehicleOwnerRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<VehicleOwner>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<VehicleOwner>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<VehicleOwner?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(VehicleOwnerInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu araç sahibi zaten var.");

        var owner = new VehicleOwner();
        Apply(owner, n);
        await _repository.CreateAsync(owner, ct);
        return owner.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, VehicleOwnerInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu araç sahibi zaten var.");

        return await _repository.UpdateAsync(id, owner =>
        {
            Apply(owner, n);
            owner.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(VehicleOwnerInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Araç sahibi kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Araç sahibi kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Araç sahibi adı zorunludur.");
    }

    private static VehicleOwnerInput Normalize(VehicleOwnerInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Tur = string.IsNullOrWhiteSpace(input.Tur) ? null : input.Tur.Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(VehicleOwner owner, VehicleOwnerInput n)
    {
        owner.Kod = n.Kod;
        owner.Ad = n.Ad;
        owner.Tur = n.Tur;
        owner.Aktif = n.Aktif;
    }
}
