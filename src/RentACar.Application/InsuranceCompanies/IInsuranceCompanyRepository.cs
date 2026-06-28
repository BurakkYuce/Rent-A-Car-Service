using RentACar.Domain.Entities;

namespace RentACar.Application.InsuranceCompanies;

public interface IInsuranceCompanyRepository
{
    Task<IReadOnlyList<InsuranceCompany>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<InsuranceCompany>> ListActiveAsync(CancellationToken ct = default);
    Task<InsuranceCompany?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(InsuranceCompany company, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<InsuranceCompany> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
