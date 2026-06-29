using RentACar.Domain.Entities;

namespace RentACar.Application.Finance;

/// <summary>Toplu nakit işleminde tek satır: belge + dengeli defter kümesi + (kira bağlıysa) tahsilat deltası.</summary>
public sealed record CashPosting(
    CashTransaction Tx, IReadOnlyList<AccountLedgerEntry> Entries, decimal RentalTahsilatDelta);

public interface ICashRepository
{
    Task<IReadOnlyList<CashTransaction>> ListAsync(CancellationToken ct = default);
    Task<CashTransaction?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Verilen işlemin zaten bir ters kaydı var mı? (idempotency).</summary>
    Task<bool> HasReversalAsync(Guid originalId, CancellationToken ct = default);

    /// <summary>
    /// Belge + DENGELİ defter kümesi + (kira bağlıysa) Tahsilat/Bakiye'yi TEK transaction'da
    /// işler. No boşluksuz tahsis edilir. Defter kayıtları immutable (DB trigger).
    /// </summary>
    Task PostAsync(
        CashTransaction tx, IReadOnlyList<AccountLedgerEntry> entries,
        decimal rentalTahsilatDelta, CancellationToken ct = default);

    /// <summary>
    /// Toplu nakit işlemi: çok satır dengeli kayıt + No tahsisi TEK transaction'da (ATOMİK hep-ya-hiç).
    /// Bir satır geçersiz/çakışırsa hiçbiri yazılmaz; No boşluğu oluşmaz. IslemAnahtari kısmi unique index
    /// → aynı toplu işlemin çift-submit'i UniqueViolation ile tüm batch'i geri alır (idempotent).
    /// </summary>
    Task PostBatchAsync(IReadOnlyList<CashPosting> items, CancellationToken ct = default);

    /// <summary>Cari bakiye (yerel para) = Σ (Borç +, Alacak −). Pozitif = müşteri borçlu.</summary>
    Task<decimal> GetCariBalanceAsync(Guid cariId, CancellationToken ct = default);

    /// <summary>Cari'nin elde tutulan depozito bakiyesi (roadmap I3): Σ Depozito (Alacak:+ Borç:−) = tutulan tutar.</summary>
    Task<decimal> GetDepozitoBakiyeAsync(Guid cariId, CancellationToken ct = default);

    /// <summary>Cari hesap ekstresi (kronolojik defter satırları).</summary>
    Task<IReadOnlyList<AccountLedgerEntry>> GetCariStatementAsync(Guid cariId, CancellationToken ct = default);
}
