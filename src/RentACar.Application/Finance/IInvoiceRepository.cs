using RentACar.Domain.Entities;

namespace RentACar.Application.Finance;

public interface IInvoiceRepository
{
    Task<IReadOnlyList<Invoice>> ListAsync(CancellationToken ct = default);
    Task<Invoice?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Fatura + satırlar + DENGELİ defter kümesini TEK transaction'da işler. No boşluksuz
    /// tahsis edilir. Fatura DB-seviyesinde değişmez (trigger).
    /// </summary>
    Task PostAsync(Invoice invoice, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);
}
