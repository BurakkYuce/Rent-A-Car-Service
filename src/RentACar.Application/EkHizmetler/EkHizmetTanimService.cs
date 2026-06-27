using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.EkHizmetler;

/// <summary>
/// Ek hizmet tanımı master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// yapılandırmadır → <see cref="Permission.OperationsWrite"/>. ListActiveAsync (kira ek hizmet
/// formu kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class EkHizmetTanimService(IEkHizmetTanimRepository repository, ICurrentUser currentUser)
{
    private readonly IEkHizmetTanimRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<EkHizmetTanim>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<EkHizmetTanim>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<EkHizmetTanim?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(EkHizmetTanimInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu ek hizmet zaten var.");

        var t = new EkHizmetTanim();
        Apply(t, n);
        await _repository.CreateAsync(t, ct);
        return t.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, EkHizmetTanimInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu ek hizmet zaten var.");

        return await _repository.UpdateAsync(id, t =>
        {
            Apply(t, n);
            t.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(EkHizmetTanimInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Ek hizmet kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Ek hizmet kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Ek hizmet adı zorunludur.");
        if (n.BirimUcret < 0) throw new ValidationException("Birim ücret negatif olamaz.");
        if (n.KdvOrani is < 0m or > 1m) throw new ValidationException("KDV oranı 0 ile 1 arasında olmalıdır.");
    }

    private static EkHizmetTanimInput Normalize(EkHizmetTanimInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        BirimUcret = input.BirimUcret,
        KdvOrani = input.KdvOrani,
        Aktif = input.Aktif
    };

    private static void Apply(EkHizmetTanim t, EkHizmetTanimInput n)
    {
        t.Kod = n.Kod;
        t.Ad = n.Ad;
        t.BirimUcret = n.BirimUcret;
        t.KdvOrani = n.KdvOrani;
        t.Aktif = n.Aktif;
    }
}
