using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.FiloKiralamalar;

/// <summary>Filo (uzun-dönem) kiralama kalıcılığı (roadmap L1). CreateAsync boşluksuz No tahsis eder.</summary>
public interface IFiloKiralamaRepository
{
    /// <summary>Sözleşmeler; <paramref name="filter"/> null → tüm kayıtlar (eski davranış).</summary>
    Task<IReadOnlyList<FiloKiralama>> ListAsync(FiloKiralamaFilter? filter = null, CancellationToken ct = default);
    Task<FiloKiralama?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(FiloKiralama row, CancellationToken ct = default);
    Task<bool> SetDurumAsync(Guid id, FiloKiraDurum durum, CancellationToken ct = default);

    /// <summary>Künye alanlarını günceller (para/süre alanları çağıran tipte YOK — bkz. FiloKiralamaMetaInput).</summary>
    Task<bool> UpdateAsync(Guid id, Action<FiloKiralama> apply, CancellationToken ct = default);
    /// <summary>F5.1 — satır kilidi + iyimser sürüm karşılaştırması (bkz. <c>IBookingRepository.UpdateReservationAsync</c>).</summary>
    Task<bool> UpdateAsync(Guid id, string? beklenenSurum, Action<FiloKiralama> apply, CancellationToken ct = default);
    /// <summary>F5.1 — satır sürümü (Postgres <c>xmin</c>, opak). Yoksa <c>null</c>.</summary>
    Task<string?> SurumAsync(Guid id, CancellationToken ct = default);
    /// <summary>F5.1 adversarial M3 — aracın şubesi (FK + metin) kapsam guard'ı için; araç yoksa <c>null</c>.</summary>
    Task<(Guid? SubeId, string? Sube)?> AracSubesiAsync(Guid vehicleId, CancellationToken ct = default);
}
