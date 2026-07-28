using RentACar.Domain.Entities;

namespace RentACar.Application.Vehicles;

public sealed record VehiclePhotoMeta(Guid Id, int Sira, string ContentType);

/// <summary>PR-3: araç fotoğraf galerisi kalıcılığı. VehiclePhoto nav'sız child (VehicleKmLog deseni) —
/// tüm sorgular VehicleId ile doğrudan.</summary>
public interface IVehiclePhotoRepository
{
    /// <summary><see cref="VehiclePhotoMeta.Sira"/> sıralı (ThenBy Id — eşit Sira'da bile deterministik).</summary>
    Task<IReadOnlyList<VehiclePhotoMeta>> ListMetaAsync(Guid vehicleId, CancellationToken ct = default);

    Task<VehiclePhoto?> FindAsync(Guid vehicleId, Guid photoId, CancellationToken ct = default);

    Task<int> CountAsync(Guid vehicleId, CancellationToken ct = default);

    /// <summary><paramref name="photo"/>.Sira YOK SAYILIR — mevcut max+1 repo içinde hesaplanır (boş
    /// koleksiyonda patlamaması için nullable-cast Max).</summary>
    Task AddAsync(VehiclePhoto photo, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid vehicleId, Guid photoId, CancellationToken ct = default);

    /// <summary><paramref name="direction"/>: -1 yukarı (küçük Sira'ya doğru) / +1 aşağı. Komşu DEĞERLE
    /// bulunur (Sira'da boşluk olabileceği için index-bazlı yanlış olurdu). Sınırda no-op.</summary>
    Task MoveAsync(Guid vehicleId, Guid photoId, int direction, CancellationToken ct = default);
}
