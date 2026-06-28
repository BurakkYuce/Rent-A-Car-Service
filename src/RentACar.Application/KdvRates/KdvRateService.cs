using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.KdvRates;

/// <summary>
/// KDV oranı master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// yapılandırmadır → <see cref="Permission.OperationsWrite"/>. Açılır liste okuması
/// (<see cref="ListActiveAsync"/>) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// Oran 0..1 doğrulanır (EkHizmetTanim.KdvOrani ile tutarlı).
/// </summary>
public sealed class KdvRateService(IKdvRateRepository repository, ICurrentUser currentUser)
{
    private readonly IKdvRateRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<KdvRate>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>Form açılır listesi kaynağı (yalnız aktif). Yetki gerektirmez.</summary>
    public Task<IReadOnlyList<KdvRate>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<KdvRate?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(KdvRateInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu KDV oranı zaten var.");

        var rate = new KdvRate();
        Apply(rate, n);
        await _repository.CreateAsync(rate, ct);
        return rate.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, KdvRateInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu KDV oranı zaten var.");

        return await _repository.UpdateAsync(id, rate =>
        {
            Apply(rate, n);
            rate.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(KdvRateInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("KDV oranı kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("KDV oranı kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("KDV oranı adı zorunludur.");
        if (n.Oran is < 0m or > 1m) throw new ValidationException("KDV oranı 0 ile 1 arasında olmalıdır (ör. 0.20 = %20).");
    }

    private static KdvRateInput Normalize(KdvRateInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Oran = input.Oran,
        Aktif = input.Aktif
    };

    private static void Apply(KdvRate rate, KdvRateInput n)
    {
        rate.Kod = n.Kod;
        rate.Ad = n.Ad;
        rate.Oran = n.Oran;
        rate.Aktif = n.Aktif;
    }
}
