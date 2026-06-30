using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.FinancialAccounts;

/// <summary>
/// Kasa/Banka hesap master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik. Döviz 3 harfe normalize.
/// </summary>
public sealed class FinancialAccountService(IFinancialAccountRepository repository, ICurrentUser currentUser)
{
    private readonly IFinancialAccountRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<FinancialAccount>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<FinancialAccount>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<FinancialAccount?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(FinancialAccountInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu hesap zaten var.");

        var account = new FinancialAccount();
        Apply(account, n);
        await _repository.CreateAsync(account, ct);
        return account.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, FinancialAccountInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu hesap zaten var.");

        return await _repository.UpdateAsync(id, account =>
        {
            Apply(account, n);
            account.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(FinancialAccountInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Hesap kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Hesap kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Hesap adı zorunludur.");
        if (n.Doviz is { Length: > 0 } d && d.Length != 3)
            throw new ValidationException("Döviz kodu 3 harf olmalıdır (ör. TRY, USD).");
    }

    private static FinancialAccountInput Normalize(FinancialAccountInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Tur = string.IsNullOrWhiteSpace(input.Tur) ? null : input.Tur.Trim(),
        Doviz = string.IsNullOrWhiteSpace(input.Doviz) ? null : input.Doviz.Trim().ToUpperInvariant(),
        Iban = string.IsNullOrWhiteSpace(input.Iban) ? null : input.Iban.Trim().ToUpperInvariant(),
        HesapNo = string.IsNullOrWhiteSpace(input.HesapNo) ? null : input.HesapNo.Trim(),
        Banka = string.IsNullOrWhiteSpace(input.Banka) ? null : input.Banka.Trim(),
        Sube = string.IsNullOrWhiteSpace(input.Sube) ? null : input.Sube.Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(FinancialAccount account, FinancialAccountInput n)
    {
        account.Kod = n.Kod;
        account.Ad = n.Ad;
        account.Tur = n.Tur;
        account.Doviz = n.Doviz;
        account.Iban = n.Iban;
        account.HesapNo = n.HesapNo;
        account.Banka = n.Banka;
        account.Sube = n.Sube;
        account.Aktif = n.Aktif;
    }
}
