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
public sealed class CurrencyService(ICurrencyRepository repository, ICurrentUser currentUser)
{
    private readonly ICurrencyRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Currency>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<Currency>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<Currency?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(CurrencyInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu döviz zaten var.");

        var cur = new Currency();
        Apply(cur, n);
        await _repository.CreateAsync(cur, ct);
        return cur.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, CurrencyInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu döviz zaten var.");

        return await _repository.UpdateAsync(id, cur =>
        {
            Apply(cur, n);
            cur.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
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
        Aktif = input.Aktif
    };

    private static void Apply(Currency cur, CurrencyInput n)
    {
        cur.Kod = n.Kod;
        cur.Ad = n.Ad;
        cur.Sembol = n.Sembol;
        cur.Aktif = n.Aktif;
    }
}
