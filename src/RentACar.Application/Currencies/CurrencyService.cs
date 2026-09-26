using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Currencies;

/// <summary>
/// Döviz master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// yapılandırmadır → <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/>
/// (form açılır liste kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class CurrencyService(ICurrencyRepository repository, ICurrentUser currentUser, ITenantCache cache)
{
    private readonly ICurrencyRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ITenantCache _cache = cache;
    private const string CK = "currencies";

    public Task<IReadOnlyList<Currency>> ListAsync(CancellationToken ct = default)
        => _cache.GetOrCreateAsync(CK, () => _repository.ListAsync(ct), ct);

    public async Task<IReadOnlyList<Currency>> ListActiveAsync(CancellationToken ct = default)
        => (await ListAsync(ct)).Where(x => x.Aktif).ToList();

    public Task<Currency?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(CurrencyInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.CodeExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu döviz zaten var.");

        var cur = new Currency();
        Apply(cur, n);
        await _repository.CreateAsync(cur, ct);
        _cache.Invalidate(CK);
        return cur.Id;
    }

    public Task<bool> UpdateAsync(Guid id, CurrencyInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input, expectedVersion: null, ct);

    /// <summary>F11.1a — full replacement with optimistic concurrency (stale version → 409 <c>cakisma</c>).</summary>
    public Task<bool> UpdateAsync(Guid id, CurrencyInput input, string expectedVersion, CancellationToken ct = default)
        => UpdateCoreAsync(id, input, expectedVersion, ct);

    /// <summary>F11.1a — opaque row version.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => _repository.GetVersionAsync(id, ct);

    /// <summary>F11.1a — versions of every row.</summary>
    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default) => _repository.GetVersionsAsync(ct);

    private async Task<bool> UpdateCoreAsync(Guid id, CurrencyInput input, string? expectedVersion, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.CodeExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu döviz zaten var.");

        void Update(Currency cur)
        {
            Apply(cur, n);
            cur.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        try
        {
            return expectedVersion is null
                ? await _repository.UpdateAsync(id, Update, ct)
                : await _repository.UpdateAsync(id, expectedVersion, Update, ct);
        }
        finally
        {
            _cache.Invalidate(CK);
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var ok = await _repository.DeleteAsync(id, ct);
        _cache.Invalidate(CK);
        return ok;
    }

    private static void Validate(CurrencyInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Döviz kodu zorunludur.");
        if (n.Kod.Length != 3) throw new ValidationException("Döviz kodu 3 harf olmalıdır (ör. TRY, USD).");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Döviz adı zorunludur.");
    }

    private static CurrencyInput Normalize(CurrencyInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Sembol = string.IsNullOrWhiteSpace(input.Sembol) ? null : input.Sembol.Trim(),
        // Normalize YENİ nesne kurar → eklenmeyen alan sessizce kaybolur.
        Ulke = string.IsNullOrWhiteSpace(input.Ulke) ? null : input.Ulke.Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(Currency cur, CurrencyInput n)
    {
        cur.Kod = n.Kod;
        cur.Ad = n.Ad;
        cur.Sembol = n.Sembol;
        cur.Ulke = n.Ulke;
        cur.Aktif = n.Aktif;
    }
}
