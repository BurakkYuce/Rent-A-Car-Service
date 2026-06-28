using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.FuelKinds;

/// <summary>
/// Yakıt türü master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class FuelKindService(IFuelKindRepository repository, ICurrentUser currentUser)
{
    private readonly IFuelKindRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<FuelKind>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<FuelKind>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<FuelKind?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(FuelKindInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu yakıt türü zaten var.");

        var kind = new FuelKind();
        Apply(kind, n);
        await _repository.CreateAsync(kind, ct);
        return kind.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, FuelKindInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu yakıt türü zaten var.");

        return await _repository.UpdateAsync(id, kind =>
        {
            Apply(kind, n);
            kind.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(FuelKindInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Yakıt türü kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Yakıt türü kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Yakıt türü adı zorunludur.");
    }

    private static FuelKindInput Normalize(FuelKindInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(FuelKind kind, FuelKindInput n)
    {
        kind.Kod = n.Kod;
        kind.Ad = n.Ad;
        kind.Aktif = n.Aktif;
    }
}
