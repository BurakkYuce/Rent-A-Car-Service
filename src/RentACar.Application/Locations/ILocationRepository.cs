using RentACar.Domain.Entities;

namespace RentACar.Application.Locations;

public interface ILocationRepository
{
    /// <summary>Tüm ofisler (yönetim ekranı). Koda göre sıralı.</summary>
    Task<IReadOnlyList<Location>> ListAsync(CancellationToken ct = default);

    /// <summary>Yalnız aktif ofisler (form açılır listesi kaynağı).</summary>
    Task<IReadOnlyList<Location>> ListActiveAsync(CancellationToken ct = default);

    /// <summary>Ada göre TEK lokasyon (C4): exact case-insensitive; aynı adda Kod sırası deterministik.</summary>
    Task<Location?> FindByNameAsync(string name, CancellationToken ct = default);

    Task<Location?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tenant içinde aynı kod (büyük/küçük harf duyarsız) başka kayıtta var mı?</summary>
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(Location location, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<Location> apply, CancellationToken ct = default);

    /// <summary>F11.1b — satır kilidi + iyimser sürüm karşılaştırması; uyuşmazlık <c>EszamanliDegisiklikException</c>.</summary>
    Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<Location> apply, CancellationToken ct = default);

    /// <summary>F11.1b — satır sürümü (Postgres <c>xmin</c>, opak). Yoksa <c>null</c>.</summary>
    Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
