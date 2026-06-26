using RentACar.Domain.Entities;

namespace RentACar.Application.Finance;

public interface ICashRepository
{
    Task<IReadOnlyList<CashTransaction>> ListAsync(CancellationToken ct = default);
    Task<CashTransaction?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Belge + DENGELİ defter kümesi + (kira bağlıysa) Tahsilat/Bakiye'yi TEK transaction'da
    /// işler. No boşluksuz tahsis edilir. Defter kayıtları immutable (DB trigger).
    /// </summary>
    Task PostAsync(
        CashTransaction tx, IReadOnlyList<AccountLedgerEntry> entries,
        decimal rentalTahsilatDelta, CancellationToken ct = default);

    /// <summary>Cari bakiye (yerel para) = Σ (Borç +, Alacak −). Pozitif = müşteri borçlu.</summary>
    Task<decimal> GetCariBalanceAsync(Guid cariId, CancellationToken ct = default);

    /// <summary>Cari hesap ekstresi (kronolojik defter satırları).</summary>
    Task<IReadOnlyList<AccountLedgerEntry>> GetCariStatementAsync(Guid cariId, CancellationToken ct = default);
}
