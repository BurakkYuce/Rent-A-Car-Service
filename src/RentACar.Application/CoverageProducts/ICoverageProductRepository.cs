using RentACar.Domain.Entities;

namespace RentACar.Application.CoverageProducts;

public interface ICoverageProductRepository
{
    /// <summary>Tüm ürünler (yönetim ekranı). Koda göre sıralı.</summary>
    Task<IReadOnlyList<CoverageProduct>> ListAsync(CancellationToken ct = default);

    /// <summary>Yalnız aktif ürünler (fiyat motoru/açılır liste kaynağı).</summary>
    Task<IReadOnlyList<CoverageProduct>> ListActiveAsync(CancellationToken ct = default);

    Task<CoverageProduct?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tenant içinde aynı kod (büyük/küçük harf duyarsız) başka kayıtta var mı?</summary>
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(CoverageProduct row, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<CoverageProduct> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
