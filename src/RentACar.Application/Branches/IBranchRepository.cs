using RentACar.Domain.Entities;

namespace RentACar.Application.Branches;

public interface IBranchRepository
{
    /// <summary>Tüm şubeler (yönetim ekranı). Koda göre sıralı.</summary>
    Task<IReadOnlyList<Branch>> ListAsync(CancellationToken ct = default);

    /// <summary>Yalnız aktif şubeler (açılır liste kaynağı).</summary>
    Task<IReadOnlyList<Branch>> ListActiveAsync(CancellationToken ct = default);

    Task<Branch?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tenant içinde aynı kod (büyük/küçük harf duyarsız) başka kayıtta var mı?</summary>
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(Branch branch, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<Branch> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
