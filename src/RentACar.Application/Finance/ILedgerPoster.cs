using RentACar.Domain.Entities;

namespace RentACar.Application.Finance;

/// <summary>
/// Belgesiz, DENGELİ defter kümesi yazıcı (No tahsisi yok). Yansıtmalar gibi doğrudan
/// defter kayıtları için. Σ Borç(base) = Σ Alacak(base) zorunlu.
/// </summary>
public interface ILedgerPoster
{
    Task PostAsync(IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);
}
