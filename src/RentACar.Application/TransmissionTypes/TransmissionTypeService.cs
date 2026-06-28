using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.TransmissionTypes;

/// <summary>
/// Vites türü master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class TransmissionTypeService(ITransmissionTypeRepository repository, ICurrentUser currentUser)
{
    private readonly ITransmissionTypeRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<TransmissionType>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<TransmissionType>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<TransmissionType?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(TransmissionTypeInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu vites türü zaten var.");

        var type = new TransmissionType();
        Apply(type, n);
        await _repository.CreateAsync(type, ct);
        return type.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, TransmissionTypeInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu vites türü zaten var.");

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

    private static void Validate(TransmissionTypeInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Vites türü kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Vites türü kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Vites türü adı zorunludur.");
    }

    private static TransmissionTypeInput Normalize(TransmissionTypeInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(TransmissionType type, TransmissionTypeInput n)
    {
        type.Kod = n.Kod;
        type.Ad = n.Ad;
        type.Aktif = n.Aktif;
    }
}
