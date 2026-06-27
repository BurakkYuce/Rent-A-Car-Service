using RentACar.Domain.Entities;

namespace RentACar.Application.DamageFiles;

public interface IDamageFileRepository
{
    Task<IReadOnlyList<DamageFile>> ListAsync(CancellationToken ct = default);
    Task<DamageFile?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>No boşluksuz tahsis edip ekler (BAF-000001).</summary>
    Task CreateAsync(DamageFile file, CancellationToken ct = default);

    /// <summary>Durum/alan güncelle (onay akışı). Mali belge değil → güncellenebilir.</summary>
    Task<bool> UpdateAsync(Guid id, Action<DamageFile> apply, CancellationToken ct = default);
}
