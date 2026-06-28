using RentACar.Domain.Entities;

namespace RentACar.Application.KdvRates;

public interface IKdvRateRepository
{
    /// <summary>Tüm KDV oranları (yönetim ekranı). Koda göre sıralı.</summary>
    Task<IReadOnlyList<KdvRate>> ListAsync(CancellationToken ct = default);

    /// <summary>Yalnız aktif oranlar (form açılır listesi kaynağı).</summary>
    Task<IReadOnlyList<KdvRate>> ListActiveAsync(CancellationToken ct = default);

    Task<KdvRate?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tenant içinde aynı kod (büyük/küçük harf duyarsız) başka kayıtta var mı?</summary>
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(KdvRate rate, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<KdvRate> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
