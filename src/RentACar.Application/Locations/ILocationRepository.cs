using RentACar.Domain.Entities;

namespace RentACar.Application.Locations;

public interface ILocationRepository
{
    /// <summary>Tüm ofisler (yönetim ekranı). Koda göre sıralı.</summary>
    Task<IReadOnlyList<Location>> ListAsync(CancellationToken ct = default);

    /// <summary>Yalnız aktif ofisler (form açılır listesi kaynağı).</summary>
    Task<IReadOnlyList<Location>> ListActiveAsync(CancellationToken ct = default);

    Task<Location?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tenant içinde aynı kod (büyük/küçük harf duyarsız) başka kayıtta var mı?</summary>
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(Location location, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<Location> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
