using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleGroups;

public interface IVehicleGroupRepository
{
    /// <summary>Tüm araç grupları (yönetim ekranı). Koda göre sıralı.</summary>
    Task<IReadOnlyList<VehicleGroup>> ListAsync(CancellationToken ct = default);

    /// <summary>Yalnız aktif gruplar (form açılır listesi kaynağı).</summary>
    Task<IReadOnlyList<VehicleGroup>> ListActiveAsync(CancellationToken ct = default);

    Task<VehicleGroup?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tenant içinde aynı kod (büyük/küçük harf duyarsız) başka kayıtta var mı?</summary>
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(VehicleGroup group, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<VehicleGroup> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
