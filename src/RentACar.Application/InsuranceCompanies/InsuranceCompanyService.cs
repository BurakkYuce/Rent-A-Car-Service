using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.InsuranceCompanies;

/// <summary>
/// Sigorta şirketi master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class InsuranceCompanyService(IInsuranceCompanyRepository repository, ICurrentUser currentUser)
{
    private readonly IInsuranceCompanyRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<InsuranceCompany>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<InsuranceCompany>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<InsuranceCompany?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(InsuranceCompanyInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu sigorta şirketi zaten var.");

        var company = new InsuranceCompany();
        Apply(company, n);
        await _repository.CreateAsync(company, ct);
        return company.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, InsuranceCompanyInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu sigorta şirketi zaten var.");

        return await _repository.UpdateAsync(id, company =>
        {
            Apply(company, n);
            company.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(InsuranceCompanyInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Sigorta şirketi kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Sigorta şirketi kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Sigorta şirketi adı zorunludur.");
    }

    private static InsuranceCompanyInput Normalize(InsuranceCompanyInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Telefon = string.IsNullOrWhiteSpace(input.Telefon) ? null : input.Telefon.Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(InsuranceCompany company, InsuranceCompanyInput n)
    {
        company.Kod = n.Kod;
        company.Ad = n.Ad;
        company.Telefon = n.Telefon;
        company.Aktif = n.Aktif;
    }
}
