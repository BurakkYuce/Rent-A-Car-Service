using RentACar.Domain.Entities;

namespace RentACar.Application.Personnel;

public interface IPersonelRepository
{
    Task<IReadOnlyList<Personel>> ListAsync(CancellationToken ct = default);
    Task<Personel?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(Personel row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Personel> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
