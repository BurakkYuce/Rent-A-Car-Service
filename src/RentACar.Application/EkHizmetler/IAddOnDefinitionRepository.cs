using RentACar.Domain.Entities;

namespace RentACar.Application.EkHizmetler;

public interface IAddOnDefinitionRepository
{
    Task<IReadOnlyList<EkHizmetTanim>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EkHizmetTanim>> ListActiveAsync(CancellationToken ct = default);
    Task<EkHizmetTanim?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(EkHizmetTanim definition, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<EkHizmetTanim> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
