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

    /// <summary>Arama/filtre + sayfalama (liste ekranı). Sube filtresi <paramref name="filter"/>'da.</summary>
    Task<Common.PagedResult<Vehicle>> SearchAsync(VehicleFilter filter, CancellationToken ct = default);

    /// <summary>
    /// FAZ-28 — detaylı liste: araç + son kredi bankası + en yakın muayene/kasko/trafik bitişi +
    /// satış ihale bilgisi + AKTİF kira (canlı çözülür, depolanmaz).
    /// </summary>
    Task<IReadOnlyList<VehicleDetayRow>> ListDetayAsync(
        VehicleDetayFilter? filter = null, CancellationToken ct = default);

    Task<Vehicle?> FindAsync(Guid id, CancellationToken ct = default);

    Task<bool> PlakaExistsAsync(string plaka, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>Yeni araç ekler. Plaka benzersizlik ihlalinde DuplicatePlakaException fırlatır.</summary>
    Task CreateAsync(Vehicle vehicle, CancellationToken ct = default);

    /// <summary>Aracı yükler, <paramref name="apply"/> ile mutasyonu uygular, kaydeder. Yoksa false.</summary>
    Task<bool> UpdateAsync(Guid id, Action<Vehicle> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>FAZ 2.5 — manuel odometre girişi: Vehicle.Km + km log satırı AYNI transaction'da.
    /// Geriye-gitme reddi TX İÇİNDE (yetkili karar — eşzamanlı girişte de tutar). Araç yoksa false.</summary>
    Task<bool> ManuelKmEkleAsync(Guid id, int km, DateTimeOffset tarih, CancellationToken ct = default);

    /// <summary>FAZ 2.5 — aracın km zaman serisi (en yeni önce, limitli; araç kartı listesi).</summary>
    Task<IReadOnlyList<VehicleKmLog>> KmLoglariAsync(Guid vehicleId, int limit = 20, CancellationToken ct = default);
}
