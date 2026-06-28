using RentACar.Domain.Entities;

namespace RentACar.Application.RateMatrices;

public interface IRateMatrixRepository
{
    /// <summary>Tüm tarife matris satırları (yönetim ekranı). Koda göre sıralı.</summary>
    Task<IReadOnlyList<RateMatrix>> ListAsync(CancellationToken ct = default);

    /// <summary>Yalnız aktif satırlar (fiyat motoru/açılır liste kaynağı).</summary>
    Task<IReadOnlyList<RateMatrix>> ListActiveAsync(CancellationToken ct = default);

    Task<RateMatrix?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tenant içinde aynı kod (büyük/küçük harf duyarsız) başka kayıtta var mı?</summary>
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(RateMatrix row, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<RateMatrix> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
