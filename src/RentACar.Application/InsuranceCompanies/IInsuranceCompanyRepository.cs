using RentACar.Domain.Entities;

namespace RentACar.Application.InsuranceCompanies;

public interface IInsuranceCompanyRepository
{
    Task<IReadOnlyList<InsuranceCompany>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<InsuranceCompany>> ListActiveAsync(CancellationToken ct = default);
    Task<InsuranceCompany?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(InsuranceCompany company, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<InsuranceCompany> apply, CancellationToken ct = default);
    /// <summary>F11.1b — satır kilidi + iyimser sürüm karşılaştırması; uyuşmazlık <c>EszamanliDegisiklikException</c>.</summary>
    Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<InsuranceCompany> apply, CancellationToken ct = default);
    /// <summary>F11.1b — satır sürümü (Postgres <c>xmin</c>, opak). Yoksa <c>null</c>.</summary>
    Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
