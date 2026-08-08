using System.Globalization;
using RentACar.Application.Authorization;
using RentACar.Domain.Common;

namespace RentACar.Application.Common;

/// <summary>
/// Kod+Ad(+Aktif) master tanım servis tabanı (denetim O12d — 10 birebir-kopya servisin ortak gövdesi):
/// liste cache (<see cref="ITenantCache"/>, tenant-kapsamlı anahtar; yazımda invalidate),
/// <see cref="Permission.OperationsWrite"/> guard, normalizasyon (Kod upper/trim, Ad trim),
/// zorunluluk + 32 karakter kod uzunluk doğrulaması ve kod benzersizliği (repo ön-kontrol + DB unique index).
/// <see cref="ListActiveAsync"/> (form açılır liste kaynağı) yetkisizdir; Aktif filtresi bellek-içi
/// (cache'lenmiş tam listeden). Tenant izolasyonu/audit alt katmanda otomatik.
/// Alt sınıflar İNCEDİR: yalnız XInput'u (kod, ad, aktif) üçlüsüne açıp Core metodları çağırır —
/// dış yüzey (somut tip adı + public imzalar + hata mesajları) değişmez.
/// </summary>
public abstract class MasterTanimService<T>(
    IMasterTanimRepository<T> repository,
    ICurrentUser currentUser,
    ITenantCache cache,
    string cacheKey,
    string adTekil) // mesajlar için: "marka", "renk", "yakıt türü"…
    where T : class, IMasterTanim, new()
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private readonly IMasterTanimRepository<T> _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ITenantCache _cache = cache;
    private readonly string _cacheKey = cacheKey;
    private readonly string _adTekil = adTekil;
    private readonly string _adTekilBas = char.ToUpper(adTekil[0], Tr) + adTekil[1..]; // "İptal sebebi" gibi (tr-TR: i→İ)

    public Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default)
        => _cache.GetOrCreateAsync(_cacheKey, () => _repository.ListAsync(ct), ct);

    public async Task<IReadOnlyList<T>> ListActiveAsync(CancellationToken ct = default)
        => (await ListAsync(ct)).Where(x => x.Aktif).ToList();

    public Task<T?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var ok = await _repository.DeleteAsync(id, ct);
        _cache.Invalidate(_cacheKey);
        return ok;
    }

    /// <param name="ekAlanlar">
    /// Kod/Ad/Aktif ÜÇLÜSÜNÜN DIŞINDAKİ alanları yazan isteğe bağlı kanca (FAZ-24). Taban hâlâ
    /// ince: ortak gövde (guard, normalizasyon, benzersizlik, cache) burada; entity'ye özgü
    /// alanlar alt sınıfın kancasında. <b>Aynı kanca Update yolunda da verilmelidir</b> — yalnız
    /// birine yazmak alanın sessizce düşmesine yol açar (kopya-kurucu tuzağı).
    /// </param>
    protected async Task<Guid> CreateCoreAsync(string? kod, string? ad, bool aktif,
        CancellationToken ct = default, Action<T>? ekAlanlar = null)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var (k, a) = Normalize(kod, ad);
        Validate(k, a);
        if (await _repository.KodExistsAsync(k, excludeId: null, ct))
            throw new ValidationException($"'{k}' kodlu {_adTekil} zaten var.");

        var entity = new T { Kod = k, Ad = a, Aktif = aktif };
        ekAlanlar?.Invoke(entity);
        await _repository.CreateAsync(entity, ct);
        _cache.Invalidate(_cacheKey);
        return entity.Id;
    }

    /// <param name="ekAlanlar">Bkz. <see cref="CreateCoreAsync"/> — iki yolda da verilmelidir.</param>
    protected async Task<bool> UpdateCoreAsync(Guid id, string? kod, string? ad, bool aktif,
        CancellationToken ct = default, Action<T>? ekAlanlar = null)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var (k, a) = Normalize(kod, ad);
        Validate(k, a);
        if (await _repository.KodExistsAsync(k, excludeId: id, ct))
            throw new ValidationException($"'{k}' kodlu {_adTekil} zaten var.");

        var ok = await _repository.UpdateAsync(id, entity =>
        {
            entity.Kod = k;
            entity.Ad = a;
            entity.Aktif = aktif;
            ekAlanlar?.Invoke(entity);
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
        _cache.Invalidate(_cacheKey);
        return ok;
    }

    private static (string Kod, string Ad) Normalize(string? kod, string? ad)
        => ((kod ?? string.Empty).Trim().ToUpperInvariant(), (ad ?? string.Empty).Trim());

    private void Validate(string kod, string ad)
    {
        if (string.IsNullOrWhiteSpace(kod)) throw new ValidationException($"{_adTekilBas} kodu zorunludur.");
        if (kod.Length > 32) throw new ValidationException($"{_adTekilBas} kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(ad)) throw new ValidationException($"{_adTekilBas} adı zorunludur.");
    }
}
