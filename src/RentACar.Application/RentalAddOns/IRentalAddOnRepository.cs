using RentACar.Domain.Entities;

namespace RentACar.Application.RentalAddOns;

public interface IRentalAddOnRepository
{
    Task<IReadOnlyList<RentalAddOn>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default);

    /// <summary>Kalemi Id ile getirir (yok → null). Servisin şube-kapsam guard'ı kirayı buradan çözer.</summary>
    Task<RentalAddOn?> FindAsync(Guid addOnId, CancellationToken ct = default);

    /// <summary>Low-B: bu idempotency anahtarıyla yazılmış kalem (RLS kapsamlı; yok → null).</summary>
    Task<RentalAddOn?> FindByOperationKeyAsync(Guid operationKey, CancellationToken ct = default);

    /// <summary>Kira için faturalanmış mı (öyleyse ek hizmet değişikliği engellenir).</summary>
    Task<bool> IsRentalInvoicedAsync(Guid rentalId, CancellationToken ct = default);

    /// <summary>Kalemi ekler VE parent kira GenelToplam/Bakiye'sini yeniden hesaplar (tek transaction).
    /// Kalemde <c>IslemAnahtari</c> varsa aynı anahtarlı kayıt kira kilidi ALTINDA yeniden aranır; varsa
    /// <see cref="RentalAddOnService.Duplicate"/> fırlatılır (eşzamanlı çift gönderim deterministik 409).</summary>
    Task AddAsync(RentalAddOn addOn, CancellationToken ct = default);

    /// <summary>Kalemi siler VE parent kira tutarlarını yeniden hesaplar. Bulunamazsa false.</summary>
    Task<bool> RemoveAsync(Guid addOnId, CancellationToken ct = default);
}
