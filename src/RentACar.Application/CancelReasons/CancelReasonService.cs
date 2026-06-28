using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.CancelReasons;

/// <summary>
/// İptal sebebi master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// yapılandırmadır → <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/>
/// (form açılır liste kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class CancelReasonService(ICancelReasonRepository repository, ICurrentUser currentUser)
{
    private readonly ICancelReasonRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<CancelReason>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<CancelReason>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<CancelReason?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(CancelReasonInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu iptal sebebi zaten var.");

        var reason = new CancelReason();
        Apply(reason, n);
        await _repository.CreateAsync(reason, ct);
        return reason.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, CancelReasonInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu iptal sebebi zaten var.");

        return await _repository.UpdateAsync(id, reason =>
        {
            Apply(reason, n);
            reason.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(CancelReasonInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("İptal sebebi kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("İptal sebebi kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("İptal sebebi adı zorunludur.");
    }

    private static CancelReasonInput Normalize(CancelReasonInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(CancelReason reason, CancelReasonInput n)
    {
        reason.Kod = n.Kod;
        reason.Ad = n.Ad;
        reason.Aktif = n.Aktif;
    }
}
