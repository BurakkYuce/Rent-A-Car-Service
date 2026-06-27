using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.PenaltyTypes;

/// <summary>
/// Ceza türü master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// yapılandırmadır → <see cref="Permission.OperationsWrite"/>. Açılır liste okuması
/// (<see cref="ListActiveAsync"/>) yetkisizdir (ceza kayıt formu çağırır). Tenant izolasyonu/audit
/// alt katmanda otomatik. VarsayilanTutar girilirse ≥ 0 doğrulanır.
/// </summary>
public sealed class PenaltyTypeService(IPenaltyTypeRepository repository, ICurrentUser currentUser)
{
    private readonly IPenaltyTypeRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<PenaltyType>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>Form açılır listesi kaynağı (yalnız aktif). Yetki gerektirmez.</summary>
    public Task<IReadOnlyList<PenaltyType>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<PenaltyType?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(PenaltyTypeInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu ceza türü zaten var.");

        var type = new PenaltyType();
        Apply(type, n);
        await _repository.CreateAsync(type, ct);
        return type.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, PenaltyTypeInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu ceza türü zaten var.");

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

    private static void Validate(PenaltyTypeInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Ceza türü kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Ceza türü kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Ceza türü adı zorunludur.");
        if (n.VarsayilanTutar is < 0m) throw new ValidationException("Varsayılan tutar negatif olamaz.");
    }

    private static PenaltyTypeInput Normalize(PenaltyTypeInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        VarsayilanTutar = input.VarsayilanTutar,
        Aktif = input.Aktif
    };

    private static void Apply(PenaltyType type, PenaltyTypeInput n)
    {
        type.Kod = n.Kod;
        type.Ad = n.Ad;
        type.VarsayilanTutar = n.VarsayilanTutar;
        type.Aktif = n.Aktif;
    }
}
