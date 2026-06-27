using RentACar.Domain.Entities;

namespace RentACar.Application.Pricing;

public interface IRateCardRepository
{
    /// <summary>Tüm tarifeler (yönetim ekranı). Grup, sonra MinGun'a göre sıralı.</summary>
    Task<IReadOnlyList<RateCard>> ListAsync(CancellationToken ct = default);

    /// <summary>Bir araç grubunun aktif tarifeleri (lookup adayları).</summary>
    Task<IReadOnlyList<RateCard>> ListByGroupAsync(string grup, CancellationToken ct = default);

    Task<RateCard?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tenant içinde aynı kod (büyük/küçük harf duyarsız) başka kayıtta var mı?</summary>
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(RateCard rateCard, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<RateCard> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
