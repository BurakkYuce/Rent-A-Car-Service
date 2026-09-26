using RentACar.Domain.Entities;

namespace RentACar.Application.Finance;

/// <summary>
/// Belgesiz, DENGELİ defter kümesi yazıcı (No tahsisi yok). Yansıtmalar gibi doğrudan
/// defter kayıtları için. Σ Borç(base) = Σ Alacak(base) zorunlu.
/// </summary>
public interface ILedgerPoster
{
    Task PostAsync(IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);

    /// <summary>
    /// FAZ-59 — defter kümesiyle AYNI transaction'da bir de defter-DIŞI künye kaydı yazar
    /// (ör. <c>CariVirmanBilgi</c>). İkisi birlikte commit olur: çift-submit'te unique kısıt
    /// hangisinde çarparsa çarpsın ikisi de geri alınır → "defter var, künye yok" ya da tersi
    /// bir durum oluşamaz.
    /// </summary>
    Task PostWithAsync<T>(IReadOnlyList<AccountLedgerEntry> entries, T extraEntry,
        CancellationToken ct = default) where T : class;
}
