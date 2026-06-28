using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Banks;

/// <summary>
/// Banka master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class BankService(IBankRepository repository, ICurrentUser currentUser)
{
    private readonly IBankRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Bank>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<Bank>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<Bank?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(BankInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu banka zaten var.");

        var bank = new Bank();
        Apply(bank, n);
        await _repository.CreateAsync(bank, ct);
        return bank.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, BankInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu banka zaten var.");

        return await _repository.UpdateAsync(id, bank =>
        {
            Apply(bank, n);
            bank.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(BankInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Banka kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Banka kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Banka adı zorunludur.");
    }

    private static BankInput Normalize(BankInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(Bank bank, BankInput n)
    {
        bank.Kod = n.Kod;
        bank.Ad = n.Ad;
        bank.Aktif = n.Aktif;
    }
}
