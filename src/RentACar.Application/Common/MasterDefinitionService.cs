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
public abstract class MasterDefinitionService<T>(
    IMasterDefinitionRepository<T> repository,
    ICurrentUser currentUser,
    ITenantCache cache,
    string cacheKey,
    string singularName) // mesajlar için: "marka", "renk", "yakıt türü"…
    where T : class, IMasterDefinition, new()
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private readonly IMasterDefinitionRepository<T> _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ITenantCache _cache = cache;
    private readonly string _cacheKey = cacheKey;
    private readonly string _singularName = singularName;
    private readonly string _singularNameCapitalized = char.ToUpper(singularName[0], Tr) + singularName[1..]; // "İptal sebebi" gibi (tr-TR: i→İ)

    public Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default)
        => _cache.GetOrCreateAsync(_cacheKey, () => _repository.ListAsync(ct), ct);

    public async Task<IReadOnlyList<T>> ListActiveAsync(CancellationToken ct = default)
        => (await ListAsync(ct)).Where(x => x.Aktif).ToList();

    public Task<T?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        try
        {
            return await _repository.DeleteAsync(id, ct);
        }
        finally
        {
            _cache.Invalidate(_cacheKey);
        }
    }

    /// <param name="extraFields">
    /// Kod/Ad/Aktif ÜÇLÜSÜNÜN DIŞINDAKİ alanları yazan isteğe bağlı kanca (FAZ-24). Taban hâlâ
    /// ince: ortak gövde (guard, normalizasyon, benzersizlik, cache) burada; entity'ye özgü
    /// alanlar alt sınıfın kancasında. <b>Aynı kanca Update yolunda da verilmelidir</b> — yalnız
    /// birine yazmak alanın sessizce düşmesine yol açar (kopya-kurucu tuzağı).
    /// </param>
    protected async Task<Guid> CreateCoreAsync(string? code, string? name, bool active,
        CancellationToken ct = default, Action<T>? extraFields = null)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var (k, a) = Normalize(code, name);
        Validate(k, a);
        if (await _repository.CodeExistsAsync(k, excludeId: null, ct))
            throw new ValidationException($"'{k}' kodlu {_singularName} zaten var.");

        var entity = new T { Kod = k, Ad = a, Aktif = active };
        extraFields?.Invoke(entity);
        await _repository.CreateAsync(entity, ct);
        _cache.Invalidate(_cacheKey);
        return entity.Id;
    }

    /// <param name="extraFields">Bkz. <see cref="CreateCoreAsync"/> — iki yolda da verilmelidir.</param>
    protected Task<bool> UpdateCoreAsync(Guid id, string? code, string? name, bool active,
        CancellationToken ct = default, Action<T>? extraFields = null)
        => UpdateCoreAsync(id, code, name, active, expectedVersion: null, ct, extraFields);

    /// <summary>
    /// F11.1a — full replacement with optimistic concurrency: <paramref name="expectedVersion"/> is compared under the
    /// row lock (<see cref="IMasterDefinitionRepository{T}.UpdateAsync(Guid, string, Action{T}, CancellationToken)"/>);
    /// mismatch → <see cref="ConcurrentModificationException"/>. <c>null</c> keeps the lock-free legacy path (Blazor).
    /// </summary>
    protected async Task<bool> UpdateCoreAsync(Guid id, string? code, string? name, bool active, string? expectedVersion,
        CancellationToken ct = default, Action<T>? extraFields = null)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var (k, a) = Normalize(code, name);
        Validate(k, a);
        if (await _repository.CodeExistsAsync(k, excludeId: id, ct))
            throw new ValidationException($"'{k}' kodlu {_singularName} zaten var.");

        void Apply(T entity)
        {
            entity.Kod = k;
            entity.Ad = a;
            entity.Aktif = active;
            extraFields?.Invoke(entity);
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        try
        {
            return expectedVersion is null
                ? await _repository.UpdateAsync(id, Apply, ct)
                : await _repository.UpdateAsync(id, expectedVersion, Apply, ct);
        }
        finally
        {
            _cache.Invalidate(_cacheKey);
        }
    }

    /// <summary>F11.1a — opaque row version for full-replacement PUTs.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
        => _repository.GetVersionAsync(id, ct);

    /// <summary>F11.1a — versions of every row (list rows carry their version).</summary>
    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default)
        => _repository.GetVersionsAsync(ct);

    private static (string Kod, string Ad) Normalize(string? code, string? name)
        => ((code ?? string.Empty).Trim().ToUpperInvariant(), (name ?? string.Empty).Trim());

    private void Validate(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ValidationException($"{_singularNameCapitalized} kodu zorunludur.");
        if (code.Length > 32) throw new ValidationException($"{_singularNameCapitalized} kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(name)) throw new ValidationException($"{_singularNameCapitalized} adı zorunludur.");
    }
}
