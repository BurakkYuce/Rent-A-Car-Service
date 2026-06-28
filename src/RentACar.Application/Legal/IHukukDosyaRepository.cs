using RentACar.Domain.Entities;

namespace RentACar.Application.Legal;

public interface IHukukDosyaRepository
{
    Task<IReadOnlyList<HukukDosya>> ListAsync(CancellationToken ct = default);
    Task<HukukDosya?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> DosyaNoExistsAsync(string dosyaNo, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(HukukDosya row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<HukukDosya> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
