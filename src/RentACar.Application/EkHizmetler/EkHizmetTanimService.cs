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
public sealed class EkHizmetTanimService(IEkHizmetTanimRepository repository, ICurrentUser currentUser, ITenantCache cache,
    IRowVersionStore? rowVersions = null)
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
        // F4.1 adversarial: SYS- öneki sistem ücret tanımlarına ayrılmış (FeeLineService repoyu doğrudan kullanır);
        // güncellemedeki kuralla tutarlı — elle SYS-* tanım açılamaz.
        if (SistemKodu(n.Kod))
            throw new ValidationException("SYS- öneki sistem ücret tanımlarına ayrılmıştır; elle tanımlanamaz.");
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

    /// <summary>F9.1 — opaque row version for the full-replacement PUT of <c>/api/ui</c>.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
        => RowVersionStoreGuard.Require(rowVersions).GetVersionAsync<EkHizmetTanim>(id, ct);

    /// <summary>F9.1 — same rules as <see cref="UpdateAsync"/> (incl. the SYS-* code fence, re-checked under the
    /// row lock) with a version check; cache invalidated after the write.</summary>
    public async Task<bool> UpdateVersionedAsync(Guid id, EkHizmetTanimInput input, string expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu ek hizmet zaten var.");
        try
        {
            return await RowVersionStoreGuard.Require(rowVersions).UpdateAsync<EkHizmetTanim>(id, expectedVersion, t =>
            {
                if ((SistemKodu(t.Kod) || SistemKodu(n.Kod)) && !string.Equals(t.Kod, n.Kod, StringComparison.OrdinalIgnoreCase))
                    throw new ValidationException("Sistem ücret tanımının (SYS-*) kodu değiştirilemez; normal tanım SYS- önekini alamaz.");
                Apply(t, n);
                t.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }, $"'{n.Kod}' kodlu ek hizmet zaten var.", ct);
        }
        finally
        {
            _cache.Invalidate(CK);
        }
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
