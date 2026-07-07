using RentACar.Domain.Entities;

namespace RentACar.Application.Finance;

public interface IInvoiceRepository
{
    Task<IReadOnlyList<Invoice>> ListAsync(CancellationToken ct = default);
    Task<Invoice?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Verilen kaynak fatura için zaten bir iade faturası kesilmiş mi? (idempotency ön-kontrol)</summary>
    Task<bool> IadeExistsForAsync(Guid kaynakFaturaId, CancellationToken ct = default);

    /// <summary>Bir kira için faturalanmış toplam BRÜT (kira dövizinde): base fatura (RentalId) + fark
    /// faturaları (KaynakKiraId), Iptal hariç. Fark faturası (PR-F1) bu toplamı günceller.</summary>
    Task<decimal> InvoicedGrossForRentalAsync(Guid rentalId, CancellationToken ct = default);

    /// <summary>
    /// Fatura + satırlar + DENGELİ defter kümesini TEK transaction'da işler. No boşluksuz
    /// tahsis edilir. Fatura DB-seviyesinde değişmez (trigger).
    /// </summary>
    Task PostAsync(Invoice invoice, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);
}
