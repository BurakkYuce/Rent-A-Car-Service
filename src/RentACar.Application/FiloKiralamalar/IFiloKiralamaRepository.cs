using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.FiloKiralamalar;

/// <summary>Filo (uzun-dönem) kiralama kalıcılığı (roadmap L1). CreateAsync boşluksuz No tahsis eder.</summary>
public interface IFiloKiralamaRepository
{
    Task<IReadOnlyList<FiloKiralama>> ListAsync(CancellationToken ct = default);
    Task<FiloKiralama?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(FiloKiralama row, CancellationToken ct = default);
    Task<bool> SetDurumAsync(Guid id, FiloKiraDurum durum, CancellationToken ct = default);
}
