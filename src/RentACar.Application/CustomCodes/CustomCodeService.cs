using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.CustomCodes;

/// <summary>
/// Özel kod master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class CustomCodeService(ICustomCodeRepository repository, ICurrentUser currentUser)
{
    private readonly ICustomCodeRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<CustomCode>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<CustomCode>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<CustomCode?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(CustomCodeInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.CodeExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu özel kod zaten var.");

        var code = new CustomCode();
        Apply(code, n);
        await _repository.CreateAsync(code, ct);
        return code.Id;
    }

    public Task<bool> UpdateAsync(Guid id, CustomCodeInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input, expectedVersion: null, ct);

    /// <summary>F11.1a — full replacement with optimistic concurrency (stale version → 409 <c>cakisma</c>).</summary>
    public Task<bool> UpdateAsync(Guid id, CustomCodeInput input, string expectedVersion, CancellationToken ct = default)
        => UpdateCoreAsync(id, input, expectedVersion, ct);

    /// <summary>F11.1a — opaque row version.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => _repository.GetVersionAsync(id, ct);

    /// <summary>F11.1a — versions of every row.</summary>
    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default) => _repository.GetVersionsAsync(ct);

    private async Task<bool> UpdateCoreAsync(Guid id, CustomCodeInput input, string? expectedVersion, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.CodeExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu özel kod zaten var.");

        void Update(CustomCode code)
        {
            Apply(code, n);
            code.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        return expectedVersion is null
            ? await _repository.UpdateAsync(id, Update, ct)
            : await _repository.UpdateAsync(id, expectedVersion, Update, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(CustomCodeInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Özel kod zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Özel kod en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Özel kod adı zorunludur.");
    }

    private static CustomCodeInput Normalize(CustomCodeInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim(),
        Turu = string.IsNullOrWhiteSpace(input.Turu) ? null : input.Turu.Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(CustomCode code, CustomCodeInput n)
    {
        code.Kod = n.Kod;
        code.Ad = n.Ad;
        code.Aciklama = n.Aciklama;
        code.Turu = n.Turu;
        code.Aktif = n.Aktif;
    }
}
