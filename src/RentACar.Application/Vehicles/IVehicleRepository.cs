using RentACar.Domain.Entities;

namespace RentACar.Application.Vehicles;

/// <summary>
/// Araç kalıcılık soyutlaması. Infrastructure, kısa-ömürlü DbContext'ler (factory)
/// üzerinden uygular. UpdateAsync, denetim (audit) eski/yeni farkının doğru
/// yakalanması için entity'yi YÜKLEYİP mutasyonu uygular (tek context içinde).
/// </summary>
public interface IVehicleRepository
{
    /// <summary><paramref name="sube"/> verilirse yalnız o şubedeki araçlar (rol bazlı kapsam).</summary>
    Task<IReadOnlyList<Vehicle>> ListAsync(string? sube = null, CancellationToken ct = default);

    Task<Vehicle?> FindAsync(Guid id, CancellationToken ct = default);

    Task<bool> PlakaExistsAsync(string plaka, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>Yeni araç ekler. Plaka benzersizlik ihlalinde DuplicatePlakaException fırlatır.</summary>
    Task CreateAsync(Vehicle vehicle, CancellationToken ct = default);

    /// <summary>Aracı yükler, <paramref name="apply"/> ile mutasyonu uygular, kaydeder. Yoksa false.</summary>
    Task<bool> UpdateAsync(Guid id, Action<Vehicle> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
