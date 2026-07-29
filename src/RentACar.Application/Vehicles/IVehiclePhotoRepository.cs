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

    /// <summary>PR-4: yalnız Id ile arama — VehicleId GEREKMEZ, izolasyon tamamen RLS'e dayanır (public-site
    /// serve uçları için; photoId tahmin edilse bile GUC başka tenant'a set edilemeyeceğinden satır dönmez).</summary>
    Task<VehiclePhoto?> FindByIdAsync(Guid photoId, CancellationToken ct = default);

    Task<int> CountAsync(Guid vehicleId, CancellationToken ct = default);

    /// <summary>PR-11 — verilen araçlardan hangilerinin EN AZ BİR fotoğrafı var (tek sorgu:
    /// <c>SELECT DISTINCT "VehicleId" … WHERE "VehicleId" = ANY(@ids)</c>). Halka açık site yayın
    /// kapısı foto şartı arar; araç başına <see cref="CountAsync"/> çağırmak rate-limit'siz en sıcak
    /// anonim sayfada N+1 üretirdi. Blob kolonu (bytea/TOAST) PROJEKSİYONA GİRMEZ.</summary>
    Task<HashSet<Guid>> ListVehicleIdsWithPhotoAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct = default);

    /// <summary><paramref name="photo"/>.Sira YOK SAYILIR — mevcut max+1 repo içinde hesaplanır (boş
    /// koleksiyonda patlamaması için nullable-cast Max).</summary>
    Task AddAsync(VehiclePhoto photo, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid vehicleId, Guid photoId, CancellationToken ct = default);

    /// <summary><paramref name="direction"/>: -1 yukarı (küçük Sira'ya doğru) / +1 aşağı. Komşu DEĞERLE
    /// bulunur (Sira'da boşluk olabileceği için index-bazlı yanlış olurdu). Sınırda no-op.</summary>
    Task MoveAsync(Guid vehicleId, Guid photoId, int direction, CancellationToken ct = default);
}
