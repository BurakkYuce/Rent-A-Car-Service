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
public sealed class EkHizmetTanimService(IEkHizmetTanimRepository repository, ICurrentUser currentUser, ITenantCache cache)
{
    private readonly IEkHizmetTanimRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ITenantCache _cache = cache;
    private const string CK = "ekhizmet";

    public Task<IReadOnlyList<EkHizmetTanim>> ListAsync(CancellationToken ct = default)
        => _cache.GetOrCreateAsync(CK, () => _repository.ListAsync(ct), ct);

    public async Task<IReadOnlyList<EkHizmetTanim>> ListActiveAsync(CancellationToken ct = default)
        => (await ListAsync(ct)).Where(x => x.Aktif).ToList();

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
        _cache.Invalidate(CK);
        return t.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, EkHizmetTanimInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu ek hizmet zaten var.");
        // F4.1 adversarial L1: SYS-* sistem ücret tanımının KODU değiştirilemez (ücret ve KDV düzenlenebilir).
        // Kod değişince tanım "sistem" olmaktan çıkıyor, kiradaki ücret satırı manuel kalem gibi silinebiliyordu.
        // Ters yön de kapalı: normal tanım SYS- önekine çevrilemez (manuel satırlar sistem satırına dönüşmesin).
        var mevcut = await _repository.FindAsync(id, ct);
        if (mevcut is not null && (SistemKodu(mevcut.Kod) || SistemKodu(n.Kod))
            && !string.Equals(mevcut.Kod, n.Kod, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Sistem ücret tanımının (SYS-*) kodu değiştirilemez; normal tanım SYS- önekini alamaz.");

        var ok = await _repository.UpdateAsync(id, t =>
        {
            Apply(t, n);
            t.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
        _cache.Invalidate(CK);
        return ok;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        // F4.1 adversarial L1: sistem ücret tanımı silinemez — FeeLineService genç/ek sürücü ve drop ücretini
        // bu tanımdan yazar; silinmesi ücret satırlarını sahipsiz bırakır.
        if (await _repository.FindAsync(id, ct) is { } t && SistemKodu(t.Kod))
            throw new ValidationException("Sistem ücret tanımı (SYS-*) silinemez.");
        var ok = await _repository.DeleteAsync(id, ct);
        _cache.Invalidate(CK);
        return ok;
    }

    private static bool SistemKodu(string? kod) => kod?.StartsWith("SYS-", StringComparison.OrdinalIgnoreCase) == true;

    private static void Validate(EkHizmetTanimInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Ek hizmet kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Ek hizmet kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Ek hizmet adı zorunludur.");
        if (n.BirimUcret < 0) throw new ValidationException("Birim ücret negatif olamaz.");
        if (n.KdvOrani is < 0m or > 1m) throw new ValidationException("KDV oranı 0 ile 1 arasında olmalıdır.");
        // 0/negatif "maks gün" hiçbir şey anlatmaz (sınırsız için null kullanılır) → reddedilir.
        if (n.MaxGun is <= 0) throw new ValidationException("Max gün pozitif olmalıdır.");
        if (n.Aciklama is { Length: > 512 }) throw new ValidationException("Açıklama en çok 512 karakter olabilir.");
    }

    private static EkHizmetTanimInput Normalize(EkHizmetTanimInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        BirimUcret = input.BirimUcret,
        KdvOrani = input.KdvOrani,
        Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim(),
        MaxGun = input.MaxGun,
        Aktif = input.Aktif
    };

    private static void Apply(EkHizmetTanim t, EkHizmetTanimInput n)
    {
        t.Kod = n.Kod;
        t.Ad = n.Ad;
        t.BirimUcret = n.BirimUcret;
        t.Aciklama = n.Aciklama;
        t.MaxGun = n.MaxGun;
        t.KdvOrani = n.KdvOrani;
        t.Aktif = n.Aktif;
    }
}
